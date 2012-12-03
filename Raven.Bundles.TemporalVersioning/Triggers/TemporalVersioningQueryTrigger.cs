﻿using System;
using System.ComponentModel.Composition;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Bundles.TemporalVersioning.Common;
using Raven.Database.Plugins;
using Raven.Database.Server;
using Raven.Json.Linq;

namespace Raven.Bundles.TemporalVersioning.Triggers
{
    [ExportMetadata("Bundle", TemporalConstants.BundleName)]
    public class TemporalVersioningQueryTrigger : AbstractReadTrigger
    {
        public override ReadVetoResult AllowRead(string key, RavenJObject metadata, ReadOperation operation, TransactionInformation transactionInformation)
        {
            // This trigger is only for simple query operations
            if (key == null || operation != ReadOperation.Query)
                return ReadVetoResult.Allowed;

            // Don't do anything if temporal versioning is inactive for this document type
            if (!Database.IsTemporalVersioningEnabled(key, metadata))
                return ReadVetoResult.Allowed;

            // If an effective date was passed in, then use it.
            DateTimeOffset effectiveDate;
            var headerValue = CurrentOperationContext.Headers.Value[TemporalConstants.EffectiveDateHeader];
            if (headerValue == null || !DateTimeOffset.TryParse(headerValue, out effectiveDate))
            {
                // If no effective data passed, return as stored.
                return ReadVetoResult.Allowed;
            }

            // Return the result if it's the active revision, or skip it otherwise.
            var temporal = metadata.GetTemporalMetadata();
            return temporal.Status == TemporalStatus.Revision &&
                   temporal.EffectiveStart <= effectiveDate && effectiveDate < temporal.EffectiveUntil &&
                   !temporal.Deleted
                       ? ReadVetoResult.Allowed
                       : ReadVetoResult.Ignore;
        }

        public override void OnRead(string key, RavenJObject document, RavenJObject metadata, ReadOperation operation,
                                    TransactionInformation transactionInformation)
        {
            // This trigger is only for simple query operations
            if (key == null || operation != ReadOperation.Query)
                return;

            // Don't do anything when temporal versioning is not enabled
            if (!Database.IsTemporalVersioningEnabled(key, metadata))
                return;

            // Only operate on temporal revisions
            var temporal = metadata.GetTemporalMetadata();
            if (temporal.Status != TemporalStatus.Revision)
                return;

            // Send back the revision number
            temporal.RevisionNumber = int.Parse(key.Split('/').Last());

            // Return the document id, not the revision id
            metadata["@id"] = key.Substring(0, key.IndexOf(TemporalConstants.TemporalKeySeparator, StringComparison.Ordinal));
        }
    }
}