// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    internal class TopRewriter : S3SqlExpressionRewriter<SearchOptions>
    {
        public static readonly TopRewriter Instance = new TopRewriter();

        private static readonly S3TableExpression TopTableExpression = new S3TableExpression(null, null, null, S3TableExpressionKind.Top);

        public override Expression VisitSqlRoot(S3SqlRootExpression expression, SearchOptions context)
        {
            if (context.CountOnly || expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            var newNormalizedPredicates = new List<S3TableExpression>(expression.TableExpressions.Count + 1);
            newNormalizedPredicates.AddRange(expression.TableExpressions);

            newNormalizedPredicates.Add(TopTableExpression);

            return new S3SqlRootExpression(newNormalizedPredicates, expression.DenormalizedExpressions);
        }
    }
}
