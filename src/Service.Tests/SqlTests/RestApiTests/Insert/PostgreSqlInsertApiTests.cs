using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlInsertApiTests : InsertApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneUniqueCharactersTest",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""NoteNum"" AS ""┬─┬ノ( º _ ºノ)"", ""DetailAssessmentAndPlanning""
                        AS ""始計"", ""WagingWar"" AS ""作戰"", ""StrategicAttack"" AS ""謀攻""
                        FROM " + _integrationUniqueCharactersTable + @"
                        WHERE ""NoteNum"" = 2
                    ) AS subq
                "
            },
            {
                "InsertOneWithMappingTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""Scientific Name"", ""region""
                            AS ""United State's Region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE ""treeId"" = 3
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND ""categoryName"" = 'FairyTales'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND book_id = 1
                    ) AS subq
                "
            },
            {
                "InsertOneWithNullFieldValue",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + (STARTING_ID_FOR_TEST_INSERTS + 1) + @" AND book_id = 2
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery1",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery2",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = '; SELECT * FROM information_schema.tables/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery3",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'value; SELECT * FROM v$version--'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery4",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'id; DROP TABLE books;'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery5",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' '' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            }
        };

        #region Overriden tests
        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            string expectedErrorMessage = "23503: insert or update on table \"books\" violates foreign key" +
                    " constraint \"book_publisher_fk\"";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );
        }
        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}