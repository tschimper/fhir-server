﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class TokenSearchParameterRowGenerator : SearchParameterRowGenerator<TokenSearchValue, V1.TokenSearchParamTableTypeRow>
    {
        private short _resourceIdSearchParamId;

        public TokenSearchParameterRowGenerator(S3StorageFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, TokenSearchValue searchValue, out V1.TokenSearchParamTableTypeRow row)
        {
            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            row = new V1.TokenSearchParamTableTypeRow(
                searchParamId,
                searchValue.System == null ? (int?)null : Model.GetSystemId(searchValue.System),
                searchValue.Code);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
