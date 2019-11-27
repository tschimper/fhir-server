﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class TransactionExceptionHandlerTests
    {
        [Fact]
        public void GivenAnOperationOutcome_WhenExecuted_ThenACorrectExceptionIsThrown()
        {
            HttpStatusCode statusCode = HttpStatusCode.Processing;
            var operationOutcome = GetOperationOutcome();
            string method = "PUT";
            string path = "Patient/12345";

            Assert.Throws<TransactionFailedException>(() => TransactionExceptionHandler.ThrowTransactionException(method, path, statusCode.ToString(), operationOutcome));
        }

        [Fact]
        public void GivenAnOperationOutcome_WhenParsed_ThenACorrectListOfOPerationOutComeIssuesIsReturned()
        {
            var operationOutcomeIssues = GetOperationOutcome().Issue;
            var parsedOperationOutcomeIssueList = TransactionExceptionHandler.GetOperationOutcomeIssues(operationOutcomeIssues);

            Assert.Equal(operationOutcomeIssues.Count, parsedOperationOutcomeIssueList.Count);

            for (int i = 0; i < operationOutcomeIssues.Count; i++)
            {
                Assert.Equal(operationOutcomeIssues[i].Severity.ToString(), parsedOperationOutcomeIssueList[i].Severity);
                Assert.Equal(operationOutcomeIssues[i].Code.ToString(), parsedOperationOutcomeIssueList[i].Code);
                Assert.Equal(operationOutcomeIssues[i].Diagnostics, parsedOperationOutcomeIssueList[i].Diagnostics);
            }
        }

        private static OperationOutcome GetOperationOutcome()
        {
            var issueComponent1 = new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Forbidden,
                Diagnostics = "Error Message1",
            };

            var issueComponent2 = new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Conflict,
                Diagnostics = "Error Message2",
            };

            return new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>() { issueComponent1, issueComponent2 },
            };
        }
    }
}