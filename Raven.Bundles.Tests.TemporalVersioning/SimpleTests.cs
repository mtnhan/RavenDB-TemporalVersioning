﻿using System;
using System.Threading;
using Raven.Bundles.Tests.TemporalVersioning.Entities;
using Raven.Client.Bundles.TemporalVersioning;
using Raven.Client.Bundles.TemporalVersioning.Common;
using Raven.Tests.Helpers;
using Xunit;

namespace Raven.Bundles.Tests.TemporalVersioning
{
    public class SimpleTests : RavenTestBase
    {
        [Fact]
        public void CanSaveLoadDateTimeOffsetFromMetadata()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                const string id = "employees/1";
                var testDateTimeOffset = new DateTimeOffset(2012, 1, 1, 8, 0, 0, TimeSpan.FromHours(-2));
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John" };
                    session.Store(employee);
                    session.Advanced.GetMetadataFor(employee).Add("TestDateTimeOffset", testDateTimeOffset);

                    session.SaveChanges();
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    var metadataCurrent = session.Advanced.GetMetadataFor(current);
                    var actual = metadataCurrent.Value<DateTimeOffset>("TestDateTimeOffset");
                    Assert.Equal(testDateTimeOffset, actual);
                    Assert.Equal(testDateTimeOffset.DateTime, actual.DateTime);
                    Assert.Equal(testDateTimeOffset.Offset, actual.Offset);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_NoEdits_Current()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                DateTimeOffset beforeSave, afterSave;

                const string id = "employees/1";
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Store(employee);

                    beforeSave = DateTimeOffset.UtcNow;
                    session.SaveChanges();
                    afterSave = DateTimeOffset.UtcNow;
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var beforeLoad = DateTimeOffset.UtcNow;
                    var current = session.Load<Employee>(id);
                    var afterLoad = DateTimeOffset.UtcNow;

                    Assert.Equal(id, current.Id);
                    Assert.Equal(10, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(1, currentTemporal.RevisionNumber);

                    Assert.NotNull(currentTemporal.Effective);
                    if (currentTemporal.Effective == null) return;
                    Assert.InRange(currentTemporal.Effective.Value, beforeLoad, afterLoad);

                    Assert.NotNull(currentTemporal.EffectiveStart);
                    if (currentTemporal.EffectiveStart == null) return;

                    Assert.InRange(currentTemporal.EffectiveStart.Value, beforeSave, afterSave);
                    Assert.Equal(DateTimeOffset.MaxValue, currentTemporal.EffectiveUntil);

                    Assert.Equal(currentTemporal.EffectiveStart, currentTemporal.AssertedStart);
                    Assert.Equal(DateTimeOffset.MaxValue, currentTemporal.AssertedUntil);

                    var history = session.Advanced.GetTemporalHistoryFor(id);
                    Assert.NotNull(history);
                    Assert.Equal(1, history.Revisions.Count);
                    var rev = history.Revisions[0];
                    Assert.Equal(id + TemporalConstants.TemporalKeySeparator + 1, rev.Key);
                    Assert.Equal(currentTemporal.EffectiveStart, rev.EffectiveStart);
                    Assert.Equal(currentTemporal.EffectiveUntil, rev.EffectiveUntil);
                    Assert.Equal(currentTemporal.AssertedStart, rev.AssertedStart);
                    Assert.Equal(currentTemporal.AssertedUntil, rev.AssertedUntil);
                    Assert.Equal(TemporalStatus.Revision, rev.Status);
                    Assert.Equal(false, rev.Deleted);
                    Assert.Equal(false, rev.Pending);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_NoEdits_Past()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                DateTimeOffset beforeSave, afterSave;

                const string id = "employees/1";
                var effectiveDate1 = new DateTimeOffset(new DateTime(2012, 1, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    beforeSave = DateTimeOffset.UtcNow;
                    session.SaveChanges();
                    afterSave = DateTimeOffset.UtcNow;
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(id, current.Id);
                    Assert.Equal(10, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(1, currentTemporal.RevisionNumber);
                    Assert.NotNull(currentTemporal.Effective);

                    var revisions = session.Advanced.GetTemporalRevisionsFor<Employee>(id, 0, 10);
                    Assert.Equal(1, revisions.Length);

                    Assert.Equal(id, revisions[0].Id);
                    Assert.Equal(10, revisions[0].PayRate);

                    var version1Temporal = session.Advanced.GetTemporalMetadataFor(revisions[0]);
                    Assert.Equal(TemporalStatus.Revision, version1Temporal.Status);
                    Assert.False(version1Temporal.Deleted);
                    Assert.Equal(effectiveDate1, version1Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version1Temporal.EffectiveUntil);
                    Assert.Equal(1, version1Temporal.RevisionNumber);

                    Assert.NotNull(version1Temporal.AssertedStart);
                    if (version1Temporal.AssertedStart == null) return;
                    Assert.InRange(version1Temporal.AssertedStart.Value, beforeSave, afterSave);
                    Assert.Equal(DateTimeOffset.MaxValue, version1Temporal.AssertedUntil);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_NoEdits_Future()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                DateTimeOffset beforeSave, afterSave;

                const string id = "employees/1";
                var effectiveDate1 = DateTimeOffset.Now.AddYears(1);
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    beforeSave = DateTimeOffset.UtcNow;
                    session.SaveChanges();
                    afterSave = DateTimeOffset.UtcNow;
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    // there should be no current revision
                    var current = session.Load<Employee>(id);
                    Assert.Null(current);

                    var revisions = session.Advanced.GetTemporalRevisionsFor<Employee>(id, 0, 10);
                    Assert.Equal(1, revisions.Length);

                    Assert.Equal(id, revisions[0].Id);
                    Assert.Equal(10, revisions[0].PayRate);

                    var version1Temporal = session.Advanced.GetTemporalMetadataFor(revisions[0]);
                    Assert.Equal(TemporalStatus.Revision, version1Temporal.Status);
                    Assert.False(version1Temporal.Deleted);
                    Assert.True(version1Temporal.Pending);
                    Assert.Equal(effectiveDate1, version1Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version1Temporal.EffectiveUntil);
                    Assert.Equal(1, version1Temporal.RevisionNumber);

                    Assert.NotNull(version1Temporal.AssertedStart);
                    if (version1Temporal.AssertedStart == null) return;
                    Assert.InRange(version1Temporal.AssertedStart.Value, beforeSave, afterSave);
                    Assert.Equal(DateTimeOffset.MaxValue, version1Temporal.AssertedUntil);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_NoEdits_Future_Activation()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                const string id = "employees/1";
                var effectiveDate1 = DateTimeOffset.Now.AddSeconds(2);
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    session.SaveChanges();
                }

                // Check the results - there shouldn't be a current doc yet.
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Null(current);
                }

                // wait for activation - allow a little extra time for the activator to complete
                Thread.Sleep(2500);

                // Check the results again - now we should have the current doc.
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.NotNull(current);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_OneEdit()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                DateTimeOffset beforeSave1, afterSave1;
                DateTimeOffset beforeSave2, afterSave2;

                const string id = "employees/1";
                var effectiveDate1 = new DateTimeOffset(new DateTime(2012, 1, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    beforeSave1 = DateTimeOffset.UtcNow;
                    session.SaveChanges();
                    afterSave1 = DateTimeOffset.UtcNow;
                }

                // Make some changes
                var effectiveDate2 = new DateTimeOffset(new DateTime(2012, 2, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate2).Load<Employee>(id);
                    employee.PayRate = 20;

                    beforeSave2 = DateTimeOffset.UtcNow;
                    session.SaveChanges();
                    afterSave2 = DateTimeOffset.UtcNow;
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(id, current.Id);
                    Assert.Equal(20, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(2, currentTemporal.RevisionNumber);

                    var revisions = session.Advanced.GetTemporalRevisionsFor<Employee>(id, 0, 10);
                    Assert.Equal(2, revisions.Length);

                    Assert.Equal(id, revisions[0].Id);
                    Assert.Equal(id, revisions[1].Id);
                    Assert.Equal(10, revisions[0].PayRate);
                    Assert.Equal(20, revisions[1].PayRate);

                    var version1Temporal = session.Advanced.GetTemporalMetadataFor(revisions[0]);
                    Assert.Equal(TemporalStatus.Revision, version1Temporal.Status);
                    Assert.False(version1Temporal.Deleted);
                    Assert.Equal(effectiveDate1, version1Temporal.EffectiveStart);
                    Assert.Equal(effectiveDate2, version1Temporal.EffectiveUntil);
                    Assert.Equal(1, version1Temporal.RevisionNumber);

                    Assert.NotNull(version1Temporal.AssertedStart);
                    Assert.NotNull(version1Temporal.AssertedUntil);
                    if (version1Temporal.AssertedStart == null || version1Temporal.AssertedUntil == null) return;
                    Assert.InRange(version1Temporal.AssertedStart.Value, beforeSave1, afterSave1);
                    Assert.InRange(version1Temporal.AssertedUntil.Value, beforeSave2, afterSave2);

                    var version2Temporal = session.Advanced.GetTemporalMetadataFor(revisions[1]);
                    Assert.Equal(TemporalStatus.Revision, version2Temporal.Status);
                    Assert.False(version2Temporal.Deleted);
                    Assert.Equal(effectiveDate2, version2Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version2Temporal.EffectiveUntil);
                    Assert.Equal(2, version2Temporal.RevisionNumber);

                    Assert.NotNull(version2Temporal.AssertedStart);
                    if (version2Temporal.AssertedStart == null) return;
                    Assert.InRange(version2Temporal.AssertedStart.Value, beforeSave2, afterSave2);
                    Assert.Equal(DateTimeOffset.MaxValue, version2Temporal.AssertedUntil);

                    var history = session.Advanced.GetTemporalHistoryFor(id);
                    Assert.NotNull(history);
                    Assert.Equal(2, history.Revisions.Count);
                    var rev1 = history.Revisions[0];
                    Assert.Equal(id + TemporalConstants.TemporalKeySeparator + 1, rev1.Key);
                    Assert.Equal(version1Temporal.EffectiveStart, rev1.EffectiveStart);
                    Assert.Equal(version1Temporal.EffectiveUntil, rev1.EffectiveUntil);
                    Assert.Equal(version1Temporal.AssertedStart, rev1.AssertedStart);
                    Assert.Equal(version1Temporal.AssertedUntil, rev1.AssertedUntil);
                    Assert.Equal(version1Temporal.Status, rev1.Status);
                    Assert.Equal(version1Temporal.Deleted, rev1.Deleted);
                    Assert.Equal(version1Temporal.Pending, rev1.Pending);
                    var rev2 = history.Revisions[1];
                    Assert.Equal(id + TemporalConstants.TemporalKeySeparator + 2, rev2.Key);
                    Assert.Equal(version2Temporal.EffectiveStart, rev2.EffectiveStart);
                    Assert.Equal(version2Temporal.EffectiveUntil, rev2.EffectiveUntil);
                    Assert.Equal(version2Temporal.AssertedStart, rev2.AssertedStart);
                    Assert.Equal(version2Temporal.AssertedUntil, rev2.AssertedUntil);
                    Assert.Equal(version2Temporal.Status, rev2.Status);
                    Assert.Equal(version2Temporal.Deleted, rev2.Deleted);
                    Assert.Equal(version2Temporal.Pending, rev2.Pending);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_OneEdit_Activation()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                const string id = "employees/1";
                var effectiveDate1 = DateTimeOffset.Now;
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    session.SaveChanges();
                }

                // Make some changes
                var effectiveDate2 = effectiveDate1.AddSeconds(2);
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate2).Load<Employee>(id);
                    employee.PayRate = 20;

                    session.SaveChanges();
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(id, current.Id);
                    Assert.Equal(10, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(1, currentTemporal.RevisionNumber);
                }

                // wait for activation - allow a little extra time for the activator to complete
                Thread.Sleep(2500);

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(id, current.Id);
                    Assert.Equal(20, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(2, currentTemporal.RevisionNumber);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_TwoEdits()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                const string id = "employees/1";
                var effectiveDate1 = new DateTimeOffset(new DateTime(2012, 1, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    session.SaveChanges();
                }

                // Make some changes
                var effectiveDate2 = new DateTimeOffset(new DateTime(2012, 2, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate2).Load<Employee>(id);
                    employee.PayRate = 20;

                    session.SaveChanges();
                }

                // Make some more changes
                var effectiveDate3 = new DateTimeOffset(new DateTime(2012, 3, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate3).Load<Employee>(id);
                    employee.PayRate = 30;

                    session.SaveChanges();
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(30, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(3, currentTemporal.RevisionNumber);

                    var revisions = session.Advanced.GetTemporalRevisionsFor<Employee>(id, 0, 10);
                    Assert.Equal(3, revisions.Length);

                    Assert.Equal(id, revisions[0].Id);
                    Assert.Equal(id, revisions[1].Id);
                    Assert.Equal(id, revisions[2].Id);
                    Assert.Equal(10, revisions[0].PayRate);
                    Assert.Equal(20, revisions[1].PayRate);
                    Assert.Equal(30, revisions[2].PayRate);

                    var version1Temporal = session.Advanced.GetTemporalMetadataFor(revisions[0]);
                    Assert.Equal(TemporalStatus.Revision, version1Temporal.Status);
                    Assert.False(version1Temporal.Deleted);
                    Assert.Equal(effectiveDate1, version1Temporal.EffectiveStart);
                    Assert.Equal(effectiveDate2, version1Temporal.EffectiveUntil);
                    Assert.Equal(1, version1Temporal.RevisionNumber);

                    var version2Temporal = session.Advanced.GetTemporalMetadataFor(revisions[1]);
                    Assert.Equal(TemporalStatus.Revision, version2Temporal.Status);
                    Assert.False(version2Temporal.Deleted);
                    Assert.Equal(effectiveDate2, version2Temporal.EffectiveStart);
                    Assert.Equal(effectiveDate3, version2Temporal.EffectiveUntil);
                    Assert.Equal(2, version2Temporal.RevisionNumber);

                    var version3Temporal = session.Advanced.GetTemporalMetadataFor(revisions[2]);
                    Assert.Equal(TemporalStatus.Revision, version3Temporal.Status);
                    Assert.False(version3Temporal.Deleted);
                    Assert.Equal(effectiveDate3, version3Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version3Temporal.EffectiveUntil);
                    Assert.Equal(3, version3Temporal.RevisionNumber);
                }
            }
        }

        [Fact]
        public void TemporalVersioning_TwoEdits_SecondOverridingFirst()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                const string id = "employees/1";
                var effectiveDate1 = new DateTimeOffset(new DateTime(2012, 1, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = new Employee { Id = id, Name = "John", PayRate = 10 };
                    session.Effective(effectiveDate1).Store(employee);

                    session.SaveChanges();
                }

                // Make some changes
                var effectiveDate2 = new DateTimeOffset(new DateTime(2012, 2, 1));
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate2).Load<Employee>(id);
                    employee.PayRate = 20;

                    session.SaveChanges();
                }

                // Make some more changes - at an earlier date than the previous edit
                var effectiveDate3 = new DateTimeOffset(new DateTime(2012, 1, 15));
                using (var session = documentStore.OpenSession())
                {
                    var employee = session.Effective(effectiveDate3).Load<Employee>(id);
                    employee.PayRate = 30;

                    session.SaveChanges();
                }

                // Check the results
                using (var session = documentStore.OpenSession())
                {
                    var current = session.Load<Employee>(id);
                    Assert.Equal(30, current.PayRate);

                    var currentTemporal = session.Advanced.GetTemporalMetadataFor(current);
                    Assert.Equal(TemporalStatus.Current, currentTemporal.Status);
                    Assert.Equal(3, currentTemporal.RevisionNumber);

                    var revisions = session.Advanced.GetTemporalRevisionsFor<Employee>(id, 0, 10);
                    Assert.Equal(3, revisions.Length);

                    Assert.Equal(id, revisions[0].Id);
                    Assert.Equal(id, revisions[1].Id);
                    Assert.Equal(id, revisions[2].Id);
                    Assert.Equal(10, revisions[0].PayRate);
                    Assert.Equal(20, revisions[1].PayRate);
                    Assert.Equal(30, revisions[2].PayRate);

                    var version1Temporal = session.Advanced.GetTemporalMetadataFor(revisions[0]);
                    Assert.Equal(TemporalStatus.Revision, version1Temporal.Status);
                    Assert.False(version1Temporal.Deleted);
                    Assert.Equal(effectiveDate1, version1Temporal.EffectiveStart);
                    Assert.Equal(effectiveDate3, version1Temporal.EffectiveUntil);
                    Assert.Equal(1, version1Temporal.RevisionNumber);

                    // the middle one now is an artifact
                    var version2Temporal = session.Advanced.GetTemporalMetadataFor(revisions[1]);
                    Assert.Equal(TemporalStatus.Artifact, version2Temporal.Status);
                    Assert.False(version2Temporal.Deleted);
                    Assert.Equal(effectiveDate2, version2Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version2Temporal.EffectiveUntil);
                    Assert.Equal(2, version2Temporal.RevisionNumber);

                    var version3Temporal = session.Advanced.GetTemporalMetadataFor(revisions[2]);
                    Assert.Equal(TemporalStatus.Revision, version3Temporal.Status);
                    Assert.False(version3Temporal.Deleted);
                    Assert.Equal(effectiveDate3, version3Temporal.EffectiveStart);
                    Assert.Equal(DateTimeOffset.MaxValue, version3Temporal.EffectiveUntil);
                    Assert.Equal(3, version3Temporal.RevisionNumber);

                    //TODO: Check temporal index to ensure artifact isn't considered
                }
            }
        }
    }
}
