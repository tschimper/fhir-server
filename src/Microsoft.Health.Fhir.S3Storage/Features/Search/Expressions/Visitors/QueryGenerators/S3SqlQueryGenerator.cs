﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class S3SqlQueryGenerator : DefaultExpressionVisitor<SearchOptions, object>, IS3SqlExpressionVisitor<SearchOptions, object>
    {
        private string _cteMainSelect; // This is represents the CTE that is the main selector for use with includes
        private List<string> _includeCtes;
        private readonly bool _isHistorySearch;
        private int _tableExpressionCounter = -1;
        private S3SqlRootExpression _rootExpression;

        public S3SqlQueryGenerator(IndentedStringBuilder sb, S3SqlQueryParameterManager parameters, S3StorageFhirModel model, bool isHistorySearch)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));

            StringBuilder = sb;
            Parameters = parameters;
            Model = model;
            _isHistorySearch = isHistorySearch;
        }

        public IndentedStringBuilder StringBuilder { get; }

        public S3SqlQueryParameterManager Parameters { get; }

        public S3StorageFhirModel Model { get; }

        public object VisitSqlRoot(S3SqlRootExpression expression, SearchOptions context)
        {
            if (!(context is SearchOptions searchOptions))
            {
                throw new ArgumentException($"Argument should be of type {nameof(SearchOptions)}", nameof(context));
            }

            _rootExpression = expression;

            if (expression.TableExpressions.Count > 0)
            {
                StringBuilder.Append("WITH ");

                StringBuilder.AppendDelimited($",{Environment.NewLine}", expression.TableExpressions, (sb, tableExpression) =>
                {
                    sb.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

                    using (sb.Indent())
                    {
                        tableExpression.AcceptVisitor(this, context);
                    }

                    sb.Append(")");
                });

                StringBuilder.AppendLine();
            }

            string resourceTableAlias = "r";

            if (searchOptions.CountOnly)
            {
                StringBuilder.AppendLine("SELECT COUNT(DISTINCT ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(")");
            }
            else
            {
                StringBuilder.Append("SELECT ");

                if (expression.TableExpressions.Count == 0)
                {
                    StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") ");
                }

                StringBuilder.Append(V1.Resource.ResourceTypeId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.ResourceId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.Version, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.IsDeleted, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.RequestMethod, resourceTableAlias).Append(", ");

                // If there's a table expression, use the previously selected bit, otherwise everything in the select is considered a match
                StringBuilder.Append(expression.TableExpressions.Count > 0 ? "CAST(IsMatch AS bit) AS IsMatch, " : "CAST(1 AS bit) AS IsMatch, ");

                StringBuilder.Append(V1.Resource.RawResource, resourceTableAlias).Append(", ");
                StringBuilder.Append(V1.Resource.IsS3ObjectValid, resourceTableAlias).Append(", ");
                StringBuilder.AppendLine(V1.Resource.LinkToRawResource, resourceTableAlias);
            }

            StringBuilder.Append("FROM ").Append(V1.Resource).Append(" ").AppendLine(resourceTableAlias);

            if (expression.TableExpressions.Count > 0)
            {
                StringBuilder.Append("INNER JOIN ").AppendLine(TableExpressionName(_tableExpressionCounter));
                StringBuilder.Append("ON ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(" = ").Append(TableExpressionName(_tableExpressionCounter)).AppendLine(".Sid1");
            }

            using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
            {
                foreach (var denormalizedPredicate in expression.DenormalizedExpressions)
                {
                    delimitedClause.BeginDelimitedElement();
                    denormalizedPredicate.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                }

                if (expression.TableExpressions.Count == 0)
                {
                    AppendHistoryClause(delimitedClause);
                    AppendDeletedClause(delimitedClause);
                }
            }

            if (!searchOptions.CountOnly)
            {
                StringBuilder.Append("ORDER BY ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" ASC");
            }

            StringBuilder.Append("OPTION(RECOMPILE)");

            return null;
        }

        private static string TableExpressionName(int id) => "cte" + id;

        public object VisitTable(S3TableExpression tableExpression, SearchOptions context)
        {
            string referenceTableAlias = "ref";
            string resourceTableAlias = "r";

            switch (tableExpression.Kind)
            {
                case S3TableExpressionKind.Normal:

                    if (tableExpression.ChainLevel == 0)
                    {
                        StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                    }
                    else
                    {
                        StringBuilder.Append("SELECT Sid1, ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table)
                            .Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.Resource.ResourceSurrogateId, null).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);

                        if (tableExpression.ChainLevel == 0)
                        {
                            // if chainLevel > 0, the intersection is already handled in the JOIN
                            AppendIntersectionWithPredecessor(delimited, tableExpression);
                        }

                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                        }

                        if (tableExpression.NormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, GetContext());
                        }
                    }

                    break;

                case S3TableExpressionKind.Concatenation:
                    StringBuilder.Append("SELECT * FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("UNION ALL");

                    goto case S3TableExpressionKind.Normal;

                case S3TableExpressionKind.All:
                    StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(V1.Resource);

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);
                        AppendDeletedClause(delimited);
                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                        }
                    }

                    break;

                case S3TableExpressionKind.NotExists:
                    StringBuilder.Append("SELECT Sid1 FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("WHERE Sid1 NOT IN").AppendLine("(");

                    using (StringBuilder.Indent())
                    {
                        StringBuilder.Append("SELECT ").AppendLine(V1.Resource.ResourceSurrogateId, null)
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                        using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                        {
                            AppendHistoryClause(delimited);

                            if (tableExpression.DenormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                            }

                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, GetContext());
                        }
                    }

                    StringBuilder.AppendLine(")");
                    break;

                case S3TableExpressionKind.Top:
                    // Everything in the top expression is considered a match
                    StringBuilder.Append("SELECT DISTINCT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).AppendLine(") Sid1, 1 AS IsMatch ")
                        .Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1))
                        .AppendLine("ORDER BY Sid1 ASC");

                    // For any includes, the source of the resource surrogate ids to join on is saved
                    _cteMainSelect = TableExpressionName(_tableExpressionCounter);

                    break;

                case S3TableExpressionKind.Chain:
                    var chainedExpression = (ChainedExpression)tableExpression.NormalizedPredicate;
                    string resourceTableAlias2 = "r2";

                    StringBuilder.Append("SELECT ");
                    if (tableExpression.ChainLevel == 1)
                    {
                        StringBuilder.Append(V1.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "Sid2" : "Sid1").Append(", ");
                    }
                    else
                    {
                        StringBuilder.Append("Sid1, ");
                    }

                    StringBuilder.Append(V1.Resource.ResourceSurrogateId, chainedExpression.Reversed && tableExpression.ChainLevel > 1 ? referenceTableAlias : resourceTableAlias).Append(" AS ").AppendLine(chainedExpression.Reversed && tableExpression.ChainLevel == 1 ? "Sid1 " : "Sid2 ")
                        .Append("FROM ").Append(V1.ReferenceSearchParam).Append(' ').AppendLine(referenceTableAlias)
                        .Append("INNER JOIN ").Append(V1.Resource).Append(' ').AppendLine(resourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceTypeId, resourceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceId, resourceTableAlias);
                    }

                    // Denormalized predicates on reverse chains need to be applied via a join to the resource table
                    if (tableExpression.DenormalizedPredicate != null && chainedExpression.Reversed)
                    {
                        StringBuilder.Append("INNER JOIN ").Append(V1.Resource).Append(' ').AppendLine(resourceTableAlias2);

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias)
                                .Append(" = ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias2);

                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(resourceTableAlias2));
                        }
                    }

                    if (tableExpression.ChainLevel > 1)
                    {
                        StringBuilder.Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.Resource.ResourceSurrogateId, chainedExpression.Reversed ? resourceTableAlias : referenceTableAlias).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.SearchParamId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainedExpression.ReferenceSearchParameter.Url)));

                        AppendHistoryClause(delimited, resourceTableAlias);
                        AppendHistoryClause(delimited, referenceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(chainedExpression.ResourceType)));

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(chainedExpression.TargetResourceType)));

                        if (tableExpression.ChainLevel == 1)
                        {
                            // if > 1, the intersection is handled by the JOIN
                            AppendIntersectionWithPredecessor(delimited, tableExpression, chainedExpression.Reversed ? resourceTableAlias : referenceTableAlias);
                        }

                        if (tableExpression.DenormalizedPredicate != null && !chainedExpression.Reversed)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(resourceTableAlias));
                        }
                    }

                    break;
                case S3TableExpressionKind.Include:
                    var includeExpression = (IncludeExpression)tableExpression.NormalizedPredicate;

                    StringBuilder.Append("SELECT DISTINCT ");
                    StringBuilder.Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" AS Sid1, 0 AS IsMatch")
                        .Append("FROM ").Append(V1.ReferenceSearchParam).Append(' ').AppendLine(referenceTableAlias)
                        .Append("INNER JOIN ").Append(V1.Resource).Append(' ').AppendLine(resourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceTypeId, resourceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceId, resourceTableAlias);
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        if (!includeExpression.WildCard)
                        {
                            delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.SearchParamId, referenceTableAlias)
                                .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(includeExpression.ReferenceSearchParameter.Url)));

                            if (includeExpression.TargetResourceType != null)
                            {
                                delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                                    .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(includeExpression.TargetResourceType)));
                            }
                        }

                        AppendHistoryClause(delimited, resourceTableAlias);
                        AppendHistoryClause(delimited, referenceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(includeExpression.ResourceType)));

                        // Limit the join to the main select CTE.
                        // The main select will have max+1 items in the result set to account for paging, so we only want to join using the max amount.
                        delimited.BeginDelimitedElement().Append(V1.Resource.ResourceSurrogateId, referenceTableAlias)
                            .Append(" IN (SELECT TOP(")
                            .Append(Parameters.AddParameter(context.MaxItemCount))
                            .Append(") Sid1 FROM ").Append(_cteMainSelect).Append(")");
                    }

                    if (_includeCtes == null)
                    {
                        _includeCtes = new List<string>();
                    }

                    _includeCtes.Add(TableExpressionName(_tableExpressionCounter));
                    break;
                case S3TableExpressionKind.IncludeUnionAll:
                    StringBuilder.AppendLine("SELECT Sid1, IsMatch");
                    StringBuilder.Append("FROM ").AppendLine(_cteMainSelect);

                    foreach (var includeCte in _includeCtes)
                    {
                        StringBuilder.AppendLine("UNION ALL");
                        StringBuilder.AppendLine("SELECT Sid1, IsMatch ");
                        StringBuilder.Append("FROM ").AppendLine(includeCte);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(tableExpression.Kind.ToString());
            }

            return null;
        }

        private SearchParameterQueryGeneratorContext GetContext(string tableAlias = null)
        {
            return new SearchParameterQueryGeneratorContext(StringBuilder, Parameters, Model, tableAlias);
        }

        private void AppendIntersectionWithPredecessor(IndentedStringBuilder.DelimitedScope delimited, S3TableExpression tableExpression, string tableAlias = null)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            if (predecessorIndex >= 0)
            {
                delimited.BeginDelimitedElement();

                string columnToSelect = (tableExpression.Kind == S3TableExpressionKind.Chain ? tableExpression.ChainLevel - 1 : tableExpression.ChainLevel) == 0 ? "Sid1" : "Sid2";

                StringBuilder.Append(V1.Resource.ResourceSurrogateId, tableAlias).Append(" IN (SELECT ").Append(columnToSelect)
                    .Append(" FROM ").Append(TableExpressionName(predecessorIndex)).Append(")");
            }
        }

        private int FindRestrictingPredecessorTableExpressionIndex()
        {
            int FindImpl(int currentIndex)
            {
                S3TableExpression currentTableExpression = _rootExpression.TableExpressions[currentIndex];
                switch (currentTableExpression.Kind)
                {
                    case S3TableExpressionKind.NotExists:
                    case S3TableExpressionKind.Normal:
                    case S3TableExpressionKind.Chain:
                        return currentIndex - 1;
                    case S3TableExpressionKind.Concatenation:
                        return FindImpl(currentIndex - 1);

                    default:
                        throw new ArgumentOutOfRangeException(currentTableExpression.Kind.ToString());
                }
            }

            return FindImpl(_tableExpressionCounter);
        }

        private void AppendDeletedClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement().Append(V1.Resource.IsDeleted, tableAlias).Append(" = 0");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append(V1.Resource.IsHistory, tableAlias).Append(" = 0");
            }
        }
    }
}