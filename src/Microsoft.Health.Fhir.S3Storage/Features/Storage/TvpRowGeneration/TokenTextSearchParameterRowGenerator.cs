// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class TokenTextSearchParameterRowGenerator : SearchParameterRowGenerator<TokenSearchValue, V1.TokenTextTableTypeRow>
    {
        public TokenTextSearchParameterRowGenerator(S3StorageFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, TokenSearchValue searchValue, out V1.TokenTextTableTypeRow row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            row = new V1.TokenTextTableTypeRow(searchParamId, searchValue.Text);
            return true;
        }
    }
}
