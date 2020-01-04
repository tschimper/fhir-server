// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewriter used to add an expression before the top clause if all of the table expressions are include expressions.
    /// Any denormalized expressions are added to the AllExpression that will be evaluated before the top expression is executed.
    /// </summary>
    internal class IncludeDenormalizedRewriter : S3SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly IncludeDenormalizedRewriter Instance = new IncludeDenormalizedRewriter();

        public override Expression VisitSqlRoot(S3SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            if (!expression.TableExpressions.All(te => te.Kind == S3TableExpressionKind.Include))
            {
                return expression;
            }

            var newNormalizedPredicates = new List<S3TableExpression>(expression.TableExpressions.Count + 1);

            Expression denormalizedExpression = Expression.And(expression.DenormalizedExpressions);
            var allExpression = new S3TableExpression(null, null, denormalizedExpression, S3TableExpressionKind.All);

            newNormalizedPredicates.Add(allExpression);
            newNormalizedPredicates.AddRange(expression.TableExpressions);

            return new S3SqlRootExpression(newNormalizedPredicates, Array.Empty<Expression>());
        }
    }
}
