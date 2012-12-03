﻿using System;
using System.Linq;
using Raven.Bundles.TemporalVersioning.Common;
using Raven.Bundles.Tests.TemporalVersioning.Entities;
using Raven.Bundles.Tests.TemporalVersioning.Indexes;
using Raven.Client.Bundles.TemporalVersioning;
using Raven.Tests.Helpers;
using Xunit;

namespace Raven.Bundles.Tests.TemporalVersioning
{
    public class StaticQueryTests : RavenTestBase
    {
        [Fact]
        public void TemporalVersioning_StaticQuery()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                documentStore.ExecuteIndex(new Employees_ByName());
                documentStore.ExecuteIndex(new Employees_CurrentByName());

                // Store a document
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
                    session.SetEffectiveDate(employee, effectiveDate2);
                    session.SaveChanges();
                }

                // Query current data non-temporally and check the results
                using (var session = documentStore.OpenSession())
                {
                    var employees = session.Query<Employee, Employees_CurrentByName>()
                                           .Customize(x => x.WaitForNonStaleResults())
                                           .Where(x => x.Name == "John")
                                           .ToList();

                    Assert.Equal(1, employees.Count);
                    var employee = employees.Single();
                    Assert.Equal(20, employee.PayRate);

                    var temporal = session.Advanced.GetTemporalMetadataFor(employee);
                    Assert.Equal(TemporalStatus.Current, temporal.Status);
                }

                // Query current data temporally and check the results
                using (var session = documentStore.OpenSession())
                {
                    var employees = session.EffectiveNow()
                                           .Query<Employee, Employees_ByName>()
                                           .Customize(x => x.WaitForNonStaleResults())
                                           .Where(x => x.Name == "John")
                                           .ToList();

                    Assert.Equal(1, employees.Count);
                    var employee = employees.Single();
                    Assert.Equal(20, employee.PayRate);

                    var temporal = session.Advanced.GetTemporalMetadataFor(employee);
                    Assert.Equal(TemporalStatus.Revision, temporal.Status);
                }

                // Query non-current data and check the results at date 1
                using (var session = documentStore.OpenSession())
                {
                    var employees = session.Effective(effectiveDate1)
                                           .Query<Employee, Employees_ByName>()
                                           .Customize(x => x.WaitForNonStaleResults())
                                           .Where(x => x.Name == "John")
                                           .ToList();

                    Assert.Equal(1, employees.Count);
                    var employee = employees.Single();

                    Assert.Equal(id, employee.Id);
                    Assert.Equal(10, employee.PayRate);
                    var temporal = session.Advanced.GetTemporalMetadataFor(employee);
                    Assert.Equal(TemporalStatus.Revision, temporal.Status);
                    Assert.Equal(1, temporal.RevisionNumber);
                }

                // Query non-current data and check the results at date 2
                using (var session = documentStore.OpenSession())
                {
                    var employees = session.Effective(effectiveDate2)
                                           .Query<Employee, Employees_ByName>()
                                           .Customize(x => x.WaitForNonStaleResults())
                                           .Where(x => x.Name == "John")
                                           .ToList();

                    var employee = employees.Single();

                    Assert.Equal(id, employee.Id);
                    Assert.Equal(20, employee.PayRate);
                    var temporal = session.Advanced.GetTemporalMetadataFor(employee);
                    Assert.Equal(TemporalStatus.Revision, temporal.Status);
                    Assert.Equal(2, temporal.RevisionNumber);
                }
            }
        }
    }
}