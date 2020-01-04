﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Turns predicates over _lastUpdated to be over ResourceSurrogateId
    /// </summary>
    internal class LastUpdatedToResourceSurrogateIdRewriter : S3SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly LastUpdatedToResourceSurrogateIdRewriter Instance = new LastUpdatedToResourceSurrogateIdRewriter();

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Name == SearchParameterNames.LastUpdated)
            {
                return Expression.MissingSearchParameter(S3SqlSearchParameters.ResourceSurrogateIdParameter, expression.IsMissing);
            }

            return expression;
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Name == SearchParameterNames.LastUpdated)
            {
                return Expression.SearchParameter(S3SqlSearchParameters.ResourceSurrogateIdParameter, expression.Expression.AcceptVisitor(this, context));
            }

            return expression;
        }

        public override Expression VisitBinary(BinaryExpression expression, object context)
        {
            if (expression.FieldName != FieldName.DateTimeStart && expression.FieldName != FieldName.DateTimeEnd)
            {
                throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            DateTime original = ((DateTimeOffset)expression.Value).UtcDateTime;
            DateTime truncated = original.TruncateToMillisecond();

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                    return Expression.GreaterThanOrEqual(
                        S3SqlFieldName.ResourceSurrogateId,
                        null,
                        S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(truncated.AddTicks(TimeSpan.TicksPerMillisecond)));
                case BinaryOperator.GreaterThanOrEqual:
                    return Expression.GreaterThanOrEqual(
                        S3SqlFieldName.ResourceSurrogateId,
                        null,
                        S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(truncated));
                case BinaryOperator.LessThan:
                    if (original == truncated)
                    {
                        return Expression.LessThanOrEqual(
                            S3SqlFieldName.ResourceSurrogateId,
                            null,
                            S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(truncated.AddTicks(-TimeSpan.TicksPerMillisecond)));
                    }

                    return Expression.LessThanOrEqual(
                        S3SqlFieldName.ResourceSurrogateId,
                        null,
                        S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(truncated));
                case BinaryOperator.LessThanOrEqual:
                    return Expression.LessThanOrEqual(
                        S3SqlFieldName.ResourceSurrogateId,
                        null,
                        S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(truncated));
                case BinaryOperator.NotEqual:
                case BinaryOperator.Equal: // expecting eq to have been rewritten as a range
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }
        }
    }
}
