// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class NumberSearchParameterRowGenerator : SearchParameterRowGenerator<NumberSearchValue, V1.NumberSearchParamTableTypeRow>
    {
        public NumberSearchParameterRowGenerator(S3StorageFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, NumberSearchValue searchValue, out V1.NumberSearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new V1.NumberSearchParamTableTypeRow(
                searchParamId,
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)V1.NumberSearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)V1.NumberSearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
