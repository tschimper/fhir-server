// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Turns an expression with a :missing=true search parameter expression and turns it into a
    /// <see cref="S3TableExpressionKind.NotExists"/> table expression with the condition negated
    /// </summary>
    internal class MissingSearchParamVisitor : S3SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly MissingSearchParamVisitor Instance = new MissingSearchParamVisitor();

        public override Expression VisitSqlRoot(S3SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            List<S3TableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.TableExpressions.Count; i++)
            {
                S3TableExpression tableExpression = expression.TableExpressions[i];

                if (tableExpression.NormalizedPredicate.AcceptVisitor(Scout.Instance, null))
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                    if (expression.TableExpressions.Count == 1)
                    {
                        // seed with all resources so that we have something to restrict
                        newTableExpressions.Add(
                            new S3TableExpression(
                                tableExpression.SearchParameterQueryGenerator,
                                null,
                                tableExpression.DenormalizedPredicate,
                                S3TableExpressionKind.All));
                    }

                    newTableExpressions.Add((S3TableExpression)tableExpression.AcceptVisitor(this, context));
                }
                else
                {
                    newTableExpressions?.Add(tableExpression);
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new S3SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }

        public override Expression VisitTable(S3TableExpression tableExpression, object context)
        {
            var normalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, context);

            return new S3TableExpression(
                tableExpression.SearchParameterQueryGenerator,
                normalizedPredicate,
                tableExpression.DenormalizedPredicate,
                S3TableExpressionKind.NotExists);
        }

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            if (expression.IsMissing)
            {
                return Expression.MissingSearchParameter(expression.Parameter, false);
            }

            return expression;
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            internal static readonly Scout Instance = new Scout();

            private Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
            {
                return expression.IsMissing;
            }
        }
    }
}
