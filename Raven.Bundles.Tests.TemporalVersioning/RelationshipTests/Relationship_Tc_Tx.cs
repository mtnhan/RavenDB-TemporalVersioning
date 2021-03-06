﻿using System;
using System.Linq;
using Raven.Bundles.Tests.TemporalVersioning.Entities;
using Raven.Bundles.Tests.TemporalVersioning.Indexes;
using Raven.Bundles.Tests.TemporalVersioning.Projections;
using Raven.Client;
using Raven.Client.Bundles.TemporalVersioning;
using Raven.Client.Linq;
using Raven.Tests.Helpers;
using Xunit;

namespace Raven.Bundles.Tests.TemporalVersioning.RelationshipTests
{
    public class Relationship_Tc_Tx : RavenTestBase
    {
        [Fact]
        public void TemporalVersioning_Relationship_Tc_Tx()
        {
            using (var documentStore = this.GetTemporalDocumentStore())
            {
                documentStore.ExecuteIndex(new CurrentEmployees_ByHiringManager());

                var effectiveDate1 = new DateTime(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var effectiveDate2 = new DateTime(2012, 2, 1, 0, 0, 0, DateTimeKind.Utc);

                using (var session = documentStore.OpenSession())
                {
                    // Alice manages both Bob and Charlie
                    session.Effective(effectiveDate1)
                           .Store(new Employee { Id = "employees/1", Name = "Alice Anderson", HireDate = effectiveDate1 });
                    session.Effective(effectiveDate1)
                           .Store(new Employee { Id = "employees/2", Name = "Bob Barker", ManagerId = "employees/1", HireDate = effectiveDate1 });
                    session.Effective(effectiveDate1)
                           .Store(new Employee { Id = "employees/3", Name = "Charlie Chaplin", ManagerId = "employees/1", HireDate = effectiveDate1 });

                    session.SaveChanges();
                }

                using (var session = documentStore.OpenSession())
                {
                    // Alice changed her last name on Feb 1, 2012
                    var employee1 = session.Effective(effectiveDate2).Load<Employee>("employees/1");
                    employee1.Name = "Alice Cooper";

                    // On the same day, Charlie became Bob's manager
                    var employee2 = session.Effective(effectiveDate2).Load<Employee>("employees/2");
                    employee2.ManagerId = "employees/3";

                    session.SaveChanges();
                }

                // Check the current results
                using (var session = documentStore.OpenSession())
                {
                    var results = session.Query<EmployeeWithManager, CurrentEmployees_ByHiringManager>()
                                         .Customize(x => x.WaitForNonStaleResults())
                                         .Customize(x=> x.DisableTemporalFiltering())
                                         .OrderBy(x => x.Name)
                                         .AsProjection<EmployeeWithManager>()
                                         .ToList();

                    Assert.Equal(3, results.Count);

                    Assert.Equal("Alice Cooper", results[0].Name);
                    Assert.Equal("Bob Barker", results[1].Name);
                    Assert.Equal("Charlie Chaplin", results[2].Name);

                    // we specifically asked for the hiring manager
                    Assert.Null(results[0].Manager);
                    Assert.Equal("Alice Anderson", results[1].Manager);
                    Assert.Equal("Alice Anderson", results[2].Manager);
                }
            }
        }
    }
}
