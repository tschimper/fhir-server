// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Reorders table expressions by expected selectivity. Most selective are moved to the front.
    /// </summary>
    internal class NormalizedPredicateReorderer : S3SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly NormalizedPredicateReorderer Instance = new NormalizedPredicateReorderer();

        public override Expression VisitSqlRoot(S3SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 1)
            {
                return expression;
            }

            List<S3TableExpression> reorderedExpressions = expression.TableExpressions.OrderByDescending(t =>
            {
                switch (t.SearchParameterQueryGenerator)
                {
                    case ReferenceSearchParameterQueryGenerator _:
                        return 10;
                    case CompartmentSearchParameterQueryGenerator _:
                        return 10;
                    default:
                        return 0;
                }
            }).ToList();

            return new S3SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }
    }
}
