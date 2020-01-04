// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Constructs a <see cref="S3SqlRootExpression"/> by partitioning predicates into normalized and denormalized predicates.
    /// </summary>
    internal class S3SqlRootExpressionRewriter : ExpressionRewriterWithInitialContext<int>
    {
        private readonly S3NormalizedSearchParameterQueryGeneratorFactory _normalizedSearchParameterQueryGeneratorFactory;

        public S3SqlRootExpressionRewriter(S3NormalizedSearchParameterQueryGeneratorFactory normalizedSearchParameterQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(normalizedSearchParameterQueryGeneratorFactory, nameof(normalizedSearchParameterQueryGeneratorFactory));
            _normalizedSearchParameterQueryGeneratorFactory = normalizedSearchParameterQueryGeneratorFactory;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, int context)
        {
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                throw new InvalidOperationException("Or is not supported as a top-level expression");
            }

            List<Expression> denormalizedPredicates = null;
            List<S3TableExpression> normalizedPredicates = null;

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];
                if (TryGetNormalizedGenerator(childExpression, out var normalizedGenerator, out var tableExpressionKind))
                {
                    EnsureAllocatedAndPopulated(ref denormalizedPredicates, expression.Expressions, i);
                    EnsureAllocatedAndPopulated(ref normalizedPredicates, Array.Empty<S3TableExpression>(), 0);

                    normalizedPredicates.Add(new S3TableExpression(normalizedGenerator, childExpression, null, tableExpressionKind, tableExpressionKind == S3TableExpressionKind.Chain ? 1 : 0));
                }
                else
                {
                    denormalizedPredicates?.Add(expression);
                }
            }

            if (normalizedPredicates == null)
            {
                S3SqlRootExpression.WithDenormalizedExpressions(expression.Expressions);
            }

            return new S3SqlRootExpression(
                normalizedPredicates ?? (IReadOnlyList<S3TableExpression>)Array.Empty<S3TableExpression>(),
                denormalizedPredicates ?? expression.Expressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitCompartment(CompartmentSearchExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitChained(ChainedExpression expression, int context)
        {
            return ConvertNonMultiary(expression);
        }

        private Expression ConvertNonMultiary(Expression expression)
        {
            return TryGetNormalizedGenerator(expression, out var generator, out var kind)
                ? S3SqlRootExpression.WithTableExpressions(new S3TableExpression(generator, normalizedPredicate: expression, denormalizedPredicate: null, kind, chainLevel: kind == S3TableExpressionKind.Chain ? 1 : 0))
                : S3SqlRootExpression.WithDenormalizedExpressions(expression);
        }

        private bool TryGetNormalizedGenerator(Expression expression, out NormalizedSearchParameterQueryGenerator normalizedGenerator, out S3TableExpressionKind kind)
        {
            normalizedGenerator = expression.AcceptVisitor(_normalizedSearchParameterQueryGeneratorFactory);
            switch (normalizedGenerator)
            {
                case ChainAnchorQueryGenerator _:
                    kind = S3TableExpressionKind.Chain;
                    break;
                case IncludeQueryGenerator _:
                    kind = S3TableExpressionKind.Include;
                    break;
                default:
                    kind = S3TableExpressionKind.Normal;
                    break;
            }

            return normalizedGenerator != null;
        }
    }
}
