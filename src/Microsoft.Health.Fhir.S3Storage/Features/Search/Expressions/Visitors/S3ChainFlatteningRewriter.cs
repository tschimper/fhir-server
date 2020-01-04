// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Flattens chained expressions into <see cref="S3SqlRootExpression"/>'s <see cref="S3SqlRootExpression.TableExpressions"/> list.
    /// The expression within a chained expression is promoted to a top-level table expression, but we keep track of the height
    /// via the <see cref="S3TableExpression.ChainLevel"/>.
    /// </summary>
    internal class S3ChainFlatteningRewriter : S3SqlExpressionRewriterWithInitialContext<(S3TableExpression containingTableExpression, int chainLevel)>
    {
        private readonly S3NormalizedSearchParameterQueryGeneratorFactory _normalizedSearchParameterQueryGeneratorFactory;

        public S3ChainFlatteningRewriter(S3NormalizedSearchParameterQueryGeneratorFactory normalizedSearchParameterQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(normalizedSearchParameterQueryGeneratorFactory, nameof(normalizedSearchParameterQueryGeneratorFactory));
            _normalizedSearchParameterQueryGeneratorFactory = normalizedSearchParameterQueryGeneratorFactory;
        }

        public override Expression VisitChained(ChainedExpression expression, (S3TableExpression containingTableExpression, int chainLevel) context)
        {
            S3TableExpression thisTableExpression;
            if (expression.Expression is ChainedExpression)
            {
                thisTableExpression = context.containingTableExpression ??
                                      new S3TableExpression(
                                          ChainAnchorQueryGenerator.Instance,
                                          expression,
                                          null,
                                          S3TableExpressionKind.Chain,
                                          context.chainLevel);

                Expression visitedExpression = expression.Expression.AcceptVisitor(this, (null, context.chainLevel + 1));

                switch (visitedExpression)
                {
                    case S3TableExpression child:
                        return Expression.And(thisTableExpression, child);
                    case MultiaryExpression multiary when multiary.MultiaryOperation == MultiaryOperator.And:
                        var tableExpressions = new List<S3TableExpression> { thisTableExpression };
                        tableExpressions.AddRange(multiary.Expressions.Cast<S3TableExpression>());
                        return Expression.And(tableExpressions);
                    default:
                        throw new InvalidOperationException("Unexpected return type");
                }
            }

            NormalizedSearchParameterQueryGenerator normalizedParameterQueryGenerator = expression.Expression.AcceptVisitor(_normalizedSearchParameterQueryGeneratorFactory);

            thisTableExpression = context.containingTableExpression;

            if (thisTableExpression == null || normalizedParameterQueryGenerator == null)
            {
                thisTableExpression = new S3TableExpression(
                    ChainAnchorQueryGenerator.Instance,
                    expression,
                    denormalizedPredicate: normalizedParameterQueryGenerator == null ? expression.Expression : null,
                    S3TableExpressionKind.Chain,
                    context.chainLevel);
            }

            if (normalizedParameterQueryGenerator == null)
            {
                return thisTableExpression;
            }

            var childTableExpression = new S3TableExpression(normalizedParameterQueryGenerator, expression.Expression, null, S3TableExpressionKind.Normal, context.chainLevel);

            return Expression.And(thisTableExpression, childTableExpression);
        }

        public override Expression VisitSqlRoot(S3SqlRootExpression expression, (S3TableExpression containingTableExpression, int chainLevel) context)
        {
            List<S3TableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.TableExpressions.Count; i++)
            {
                S3TableExpression tableExpression = expression.TableExpressions[i];
                if (tableExpression.Kind != S3TableExpressionKind.Chain)
                {
                    newTableExpressions?.Add(tableExpression);
                    continue;
                }

                Expression visitedNormalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, (tableExpression, tableExpression.ChainLevel));
                switch (visitedNormalizedPredicate)
                {
                    case S3TableExpression convertedExpression:
                        EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);
                        newTableExpressions.Add(convertedExpression);
                        break;
                    case MultiaryExpression multiary when multiary.MultiaryOperation == MultiaryOperator.And:
                        EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                        newTableExpressions.AddRange(multiary.Expressions.Cast<S3TableExpression>());
                        break;
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new S3SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }
    }
}
