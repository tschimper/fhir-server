// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    internal abstract class S3SqlExpressionRewriter<TContext> : ExpressionRewriter<TContext>, IS3SqlExpressionVisitor<TContext, Expression>
    {
        public virtual Expression VisitSqlRoot(S3SqlRootExpression expression, TContext context)
        {
            IReadOnlyList<Expression> denormalizedPredicates = VisitArray(expression.DenormalizedExpressions, context);
            IReadOnlyList<S3TableExpression> normalizedPredicates = VisitArray(expression.TableExpressions, context);

            if (ReferenceEquals(normalizedPredicates, expression.TableExpressions) &&
                ReferenceEquals(denormalizedPredicates, expression.DenormalizedExpressions))
            {
                return expression;
            }

            return new S3SqlRootExpression(normalizedPredicates, denormalizedPredicates);
        }

        public virtual Expression VisitTable(S3TableExpression tableExpression, TContext context)
        {
            Expression denormalizedPredicate = tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
            Expression normalizedPredicate = tableExpression.NormalizedPredicate?.AcceptVisitor(this, context);

            if (ReferenceEquals(denormalizedPredicate, tableExpression.DenormalizedPredicate) &&
                ReferenceEquals(normalizedPredicate, tableExpression.NormalizedPredicate))
            {
                return tableExpression;
            }

            return new S3TableExpression(tableExpression.SearchParameterQueryGenerator, normalizedPredicate, denormalizedPredicate, tableExpression.Kind);
        }
    }
}
