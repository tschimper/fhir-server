// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;

// using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AngleSharp.Common;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search
{
    internal class S3StorageSearchService : SearchService
    {
        private readonly S3StorageFhirModel _model;
        private readonly S3SqlRootExpressionRewriter _sqlRootExpressionRewriter;
        private readonly S3ChainFlatteningRewriter _chainFlatteningRewriter;
        private readonly S3StringOverflowRewriter _stringOverflowRewriter;
        private readonly S3StorageDataStoreConfiguration _configuration;
        private readonly ILogger<S3StorageSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly S3SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;

        internal static readonly Encoding SQLResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        internal static readonly Encoding S3ResourceEncoding = new UTF8Encoding();

        // fprivate AWSCredentials _awsCredentials;
        private static AmazonS3Client client;
        private string _awsBucket;
        private System.Uri _awsHost;
        private RegionEndpoint bucketRegion = RegionEndpoint.USWest2;
        private AmazonS3Config s3Config = new AmazonS3Config();

        // Specify your bucket region (an example region is shown).
        // private static AwsRegion _awsbucketRegion;

        public S3StorageSearchService(
            S3StorageDataStoreConfiguration configuration,
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            S3StorageFhirModel model,
            S3SqlRootExpressionRewriter sqlRootExpressionRewriter,
            S3ChainFlatteningRewriter chainFlatteningRewriter,
            S3StringOverflowRewriter stringOverflowRewriter,
            S3SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<S3StorageSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore)
        {
            EnsureArg.IsNotNull(sqlRootExpressionRewriter, nameof(sqlRootExpressionRewriter));
            EnsureArg.IsNotNull(chainFlatteningRewriter, nameof(chainFlatteningRewriter));
            EnsureArg.IsNotNull(stringOverflowRewriter, nameof(stringOverflowRewriter));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _model = model;
            _sqlRootExpressionRewriter = sqlRootExpressionRewriter;
            _chainFlatteningRewriter = chainFlatteningRewriter;
            _stringOverflowRewriter = stringOverflowRewriter;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
            _configuration = configuration;

            _awsHost = _configuration.S3StorageURL;

            // _awsCredentials = new AwsCredential(_configuration.AuthentificationString, _configuration.SecretString);
            // _awsbucketRegion = AwsRegion.EUCentral1;
            _awsBucket = "kfhfhir";
        }

        protected override async Task<SearchResult> SearchInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SearchResult searchResult;

            // If we should include the total count of matching search results
            if (searchOptions.IncludeTotal == TotalType.Accurate && !searchOptions.CountOnly)
            {
                searchResult = await SearchImpl(searchOptions, false, cancellationToken);

                // If this is the first page and there aren't any more pages
                if (searchOptions.ContinuationToken == null && searchResult.ContinuationToken == null)
                {
                    // Count the results on the page.
                    searchResult.TotalCount = searchResult.Results.Count();
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        searchOptions.CountOnly = true;

                        // And perform a second read.
                        var countOnlySearchResult = await SearchImpl(searchOptions, false, cancellationToken);

                        searchResult.TotalCount = countOnlySearchResult.TotalCount;
                    }
                    finally
                    {
                        // Ensure search options is set to its original state.
                        searchOptions.CountOnly = false;
                    }
                }
            }
            else
            {
                searchResult = await SearchImpl(searchOptions, false, cancellationToken);
            }

            return searchResult;
        }

        protected override async Task<SearchResult> SearchHistoryInternalAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            return await SearchImpl(searchOptions, true, cancellationToken);
        }

        private async Task<SearchResult> SearchImpl(SearchOptions searchOptions, bool historySearch, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            Expression searchExpression = searchOptions.Expression;

            // AND in the continuation token
            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken) && !searchOptions.CountOnly)
            {
                if (long.TryParse(searchOptions.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
                {
                    var tokenExpression = Expression.SearchParameter(S3SqlSearchParameters.ResourceSurrogateIdParameter, Expression.GreaterThan(S3SqlFieldName.ResourceSurrogateId, null, token));
                    searchExpression = searchExpression == null ? tokenExpression : (Expression)Expression.And(tokenExpression, searchExpression);
                }
                else
                {
                    throw new BadRequestException(Resources.InvalidContinuationToken);
                }
            }

            S3SqlRootExpression expression = (S3SqlRootExpression)searchExpression
                                               ?.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance)
                                               .AcceptVisitor(DateTimeEqualityRewriter.Instance)
                                               .AcceptVisitor(FlatteningRewriter.Instance)
                                               .AcceptVisitor(_sqlRootExpressionRewriter)
                                               .AcceptVisitor(DenormalizedPredicateRewriter.Instance)
                                               .AcceptVisitor(NormalizedPredicateReorderer.Instance)
                                               .AcceptVisitor(_chainFlatteningRewriter)
                                               .AcceptVisitor(DateTimeBoundedRangeRewriter.Instance)
                                               .AcceptVisitor(_stringOverflowRewriter)
                                               .AcceptVisitor(NumericRangeRewriter.Instance)
                                               .AcceptVisitor(MissingSearchParamVisitor.Instance)
                                               .AcceptVisitor(IncludeDenormalizedRewriter.Instance)
                                               .AcceptVisitor(TopRewriter.Instance, searchOptions)
                                               .AcceptVisitor(IncludeRewriter.Instance)
                                           ?? S3SqlRootExpression.WithDenormalizedExpressions();

            using (S3SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                var stringBuilder = new IndentedStringBuilder(new StringBuilder());

                EnableTimeAndIoMessageLogging(stringBuilder, sqlConnectionWrapper);

                var queryGenerator = new S3SqlQueryGenerator(stringBuilder, new S3SqlQueryParameterManager(sqlCommand.Parameters), _model, historySearch);

                expression.AcceptVisitor(queryGenerator, searchOptions);

                sqlCommand.CommandText = stringBuilder.ToString();

                LogSqlCommand(sqlCommand);

                using (var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (searchOptions.CountOnly)
                    {
                        await reader.ReadAsync(cancellationToken);
                        return new SearchResult(reader.GetInt32(0), searchOptions.UnsupportedSearchParams);
                    }

                    var resources = new List<SearchResultEntry>(searchOptions.MaxItemCount);
                    long? newContinuationId = null;
                    bool moreResults = false;
                    int matchCount = 0;

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (short resourceTypeId, string resourceId, int version, bool isDeleted, long resourceSurrogateId, string requestMethod, bool isMatch, Stream rawResourceStream, bool isS3ObjectValid, string linkToRawResource) = reader.ReadRow(
                            V1.Resource.ResourceTypeId,
                            V1.Resource.ResourceId,
                            V1.Resource.Version,
                            V1.Resource.IsDeleted,
                            V1.Resource.ResourceSurrogateId,
                            V1.Resource.RequestMethod,
                            _isMatch,
                            V1.Resource.RawResource,
                            V1.Resource.IsS3ObjectValid,
                            V1.Resource.LinkToRawResource);

                        // If we get to this point, we know there are more results so we need a continuation token
                        // Additionally, this resource shouldn't be included in the results
                        if (matchCount >= searchOptions.MaxItemCount && isMatch)
                        {
                            moreResults = true;
                            continue;
                        }

                        // See if this resource is a continuation token candidate and increase the count
                        if (isMatch)
                        {
                            newContinuationId = resourceSurrogateId;
                            matchCount++;
                        }

                        string rawResource = null;
                        try
                        {
                            if (isS3ObjectValid)
                            {
                                Console.WriteLine("S3 Object Valid");
                                Console.WriteLine(V1.Resource.LinkToRawResource);
                                CreatClient();
                                using (GetObjectResponse response = await client.GetObjectAsync(_awsBucket, linkToRawResource))
                                using (Stream responseStream = response.ResponseStream)
                                using (StreamReader s3reader = new StreamReader(responseStream, S3ResourceEncoding))
                                {
                                    // The following outputs the content of my text file:
                                    // string content = reader.ReadToEnd();
                                    // client = new S3Client(_awsbucketRegion, _awsCredentials);
                                    // GetObjectRequest request = new GetObjectRequest(client.Host.ToString(), _awsBucket, linkToRawResource);

                                    // using (S3Object response = await client.GetObjectAsync(request))
                                    // {
                                    //  var responseStream = response.OpenAsync();
                                    //  using (StreamReader reader = new StreamReader(responseStream.Result, S3ResourceEncoding))
                                    // {
                                    rawResource = s3reader.ReadToEnd(); // Now you process the response body.

                                    // }
                                }
                            }
                            else
                            {
                                Console.WriteLine("S3 Object not valid, get Data from SQL Database");
                                using (rawResourceStream)
                                using (var gzipStream = new GZipStream(rawResourceStream, CompressionMode.Decompress))
                                using (var s3SQLreader = new StreamReader(gzipStream, SQLResourceEncoding))
                                {
                                    rawResource = await s3SQLreader.ReadToEndAsync();
                                }
                            }
                        }
                        catch (AmazonS3Exception e)
                        {
                            _logger.LogWarning(e, "Failed to read to the S3 data {Exception}", e.Message);
                        }

                        resources.Add(new SearchResultEntry(
                            new ResourceWrapper(
                                resourceId,
                                version.ToString(CultureInfo.InvariantCulture),
                                _model.GetResourceTypeName(resourceTypeId),
                                new RawResource(rawResource, FhirResourceFormat.Json),
                                new ResourceRequest(requestMethod),
                                new DateTimeOffset(S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                                isDeleted,
                                null,
                                null,
                                null),
                            isMatch ? SearchEntryMode.Match : SearchEntryMode.Include));
                    }

                    // call NextResultAsync to get the info messages
                    await reader.NextResultAsync(cancellationToken);

                    IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters;
                    if (searchOptions.Sort?.Count > 0)
                    {
                        // we don't currently support sort
                        unsupportedSortingParameters = searchOptions.UnsupportedSortingParams.Concat(searchOptions.Sort.Select(s => (s.searchParameterInfo.Name, Core.Resources.SortNotSupported))).ToList();
                    }
                    else
                    {
                        unsupportedSortingParameters = searchOptions.UnsupportedSortingParams;
                    }

                    return new SearchResult(resources, searchOptions.UnsupportedSearchParams, unsupportedSortingParameters, moreResults ? newContinuationId.Value.ToString(CultureInfo.InvariantCulture) : null);
                }
            }
        }

        [Conditional("DEBUG")]
        private void EnableTimeAndIoMessageLogging(IndentedStringBuilder stringBuilder, S3SqlConnectionWrapper sqlConnectionWrapper)
        {
            stringBuilder.AppendLine("SET STATISTICS IO ON;");
            stringBuilder.AppendLine("SET STATISTICS TIME ON;");
            stringBuilder.AppendLine();
            sqlConnectionWrapper.S3SqlConnection.InfoMessage += (sender, args) => _logger.LogInformation($"SQL message: {args.Message}");
        }

        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        private void LogSqlCommand(SqlCommand sqlCommand)
        {
            var sb = new StringBuilder();
            foreach (SqlParameter p in sqlCommand.Parameters)
            {
                sb.Append("DECLARE ")
                    .Append(p)
                    .Append(" ")
                    .Append(p.SqlDbType)
                    .Append(p.Value is string ? $"({p.Size})" : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                    .Append(" = ")
                    .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                    .Append(p.Value is string || p.Value is DateTime ? $"'{p.Value}'" : p.Value.ToString())
                    .AppendLine(";");
            }

            sb.AppendLine();

            sb.AppendLine(sqlCommand.CommandText);
            _logger.LogInformation(sb.ToString());
        }

        private void CreatClient()
        {
            // RegionEndpoint bucketRegion = RegionEndpoint.USWest2;
            // AmazonS3Config s3Config = new AmazonS3Config();

            if (_configuration.S3Type == "AWS")
            {
                bucketRegion = RegionEndpoint.EUCentral1;
                s3Config.RegionEndpoint = bucketRegion;
                s3Config.ServiceURL = "https://s3.eu-central-1.amazonaws.com";
            }
            else
            {
                bucketRegion = RegionEndpoint.USWest2;
                s3Config.ServiceURL = _configuration.S3StorageURL.ToString();
            }

            // s3Config.RegionEndpoint = bucketRegion;

            // s3Config.ServiceURL = _configuration.S3StorageURL.ToString();
            s3Config.UseHttp = false;
            if (client == null)
            {
                client = new AmazonS3Client(_configuration.AuthentificationString, _configuration.SecretString, s3Config);
            }
        }
    }
}
