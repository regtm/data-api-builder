using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests
{
    public class SqlTestHelper : TestHelper
    {
        // This is is the key which holds all the rows in the response
        // for REST requests.
        public static readonly string jsonResultTopLevelKey = "value";
        public static void RemoveAllRelationshipBetweenEntities(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities.ToList())
            {
                Entity updatedEntity = new(entity.Source, entity.Rest,
                                           entity.GraphQL, entity.Permissions,
                                           Relationships: null, Mappings: null);
                runtimeConfig.Entities.Remove(entityName);
                runtimeConfig.Entities.Add(entityName, updatedEntity);
            }
        }

        /// <summary>
        /// Converts strings to JSON objects and does a deep compare
        /// </summary>
        /// <remarks>
        /// This method of comparing JSON-s provides:
        /// <list type="number">
        /// <item> Insesitivity to spaces in the JSON formatting </item>
        /// <item> Insesitivity to order for elements in dictionaries. E.g. {"a": 1, "b": 2} = {"b": 2, "a": 1} </item>
        /// <item> Sensitivity to order for elements in lists. E.g. [{"a": 1}, {"b": 2}] ~= [{"b": 2}, {"a": 1}] </item>
        /// </list>
        /// In contrast, string comparing does not provide 1 and 2.
        /// </remarks>
        /// <param name="jsonString1"></param>
        /// <param name="jsonString2"></param>
        /// <returns>True if JSON objects are the same</returns>
        public static bool JsonStringsDeepEqual(string jsonString1, string jsonString2)
        {
            return string.IsNullOrEmpty(jsonString1) && string.IsNullOrEmpty(jsonString2) ||
                JToken.DeepEquals(JToken.Parse(jsonString1), JToken.Parse(jsonString2));
        }

        /// <summary>
        /// Adds a useful failure message around the excpeted == actual operation
        /// <summary>
        public static void PerformTestEqualJsonStrings(string expected, string actual)
        {
            Assert.IsTrue(JsonStringsDeepEqual(expected, actual),
            $"\nExpected:<{expected}>\nActual:<{actual}>");
        }

        /// <summary>
        /// Tests for different aspects of the error in a GraphQL response
        /// </summary>
        public static void TestForErrorInGraphQLResponse(string response, string message = null, string statusCode = null, string path = null)
        {
            Console.WriteLine(response);

            if (message is not null)
            {
                Console.WriteLine(response);
                Assert.IsTrue(response.Contains(message), $"Message \"{message}\" not found in error");
            }

            if (statusCode != null)
            {
                Assert.IsTrue(response.Contains($"\"code\":\"{statusCode}\""), $"Status code \"{statusCode}\" not found in error");
            }

            if (path is not null)
            {
                Console.WriteLine(response);
                Assert.IsTrue(response.Contains(path), $"Path \"{path}\" not found in error");
            }
        }

        /// <summary>
        /// Verifies the ActionResult is as expected with the expected status code.
        /// </summary>
        /// <param name="expected">Expected result of the query execution.</param>
        /// <param name="request">The HttpRequestMessage sent to the engine via HttpClient.</param>
        /// <param name="response">The HttpResponseMessage returned by the engine.</param>
        /// <param name="exceptionExpected">Boolean value indicating whether an exception is expected as
        /// a result of executing the request on the engine.</param>
        /// <param name="httpMethod">The http method specified in the request.</param>
        /// <param name="expectedLocationHeader">The expected location header in the response(if any).</param>
        /// <param name="verifyNumRecords"></param>
        /// <returns></returns>
        public static async Task VerifyResultAsync(
            string expected,
            HttpRequestMessage request,
            HttpResponseMessage response,
            bool exceptionExpected,
            HttpMethod httpMethod,
            string expectedLocationHeader,
            int verifyNumRecords)
        {
            string responseBody = await response.Content.ReadAsStringAsync();
            if (!exceptionExpected)
            {
                // Assert that the expectedLocation and actualLocation are equal in case of
                // POST operation.
                if (httpMethod == HttpMethod.Post)
                {
                    // Find the actual location using the response and request uri.
                    // Response uri = Request uri + "/" + actualLocation
                    // For eg. POST Request URI: http://localhost/api/Review
                    // 201 Created Response URI: http://localhost/api/Review/book_id/1/id/5001
                    // therefore, actualLocation = book_id/1/id/5001
                    string responseUri = (response.Headers.Location.OriginalString);
                    string requestUri = request.RequestUri.OriginalString;
                    string actualLocation = responseUri.Substring(requestUri.Length + 1);
                    Assert.AreEqual(expectedLocationHeader, actualLocation);
                }

                // Assert the number of records received is equal to expected number of records.
                if (response.StatusCode is HttpStatusCode.OK && verifyNumRecords >= 0)
                {
                    Dictionary<string, JsonElement[]> actualAsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(responseBody);
                    Assert.AreEqual(actualAsDict[jsonResultTopLevelKey].Length, verifyNumRecords);
                }

                Assert.IsTrue(JsonStringsDeepEqual(expected, responseBody));
            }
            else
            {
                // Quote(") has to be treated differently than other unicode characters
                // as it has to be added with a preceding backslash.
                responseBody = Regex.Replace(responseBody, @"\\u0022", @"\\""");

                // Convert the escaped characters into their unescaped form.
                responseBody = Regex.Unescape(responseBody);
                Assert.AreEqual(expected, responseBody);
            }
        }

        /// <summary>
        /// Helper method to get the HttpMethod based on the operation type.
        /// </summary>
        /// <param name="operationType">The operation to be executed on the entity.</param>
        /// <returns></returns>
        /// <exception cref="DataApiBuilderException"></exception>
        public static HttpMethod GetHttpMethodFromOperation(Operation operationType)
        {
            switch (operationType)
            {
                case Operation.Read:
                    return HttpMethod.Get;
                case Operation.Insert:
                    return HttpMethod.Post;
                case Operation.Delete:
                    return HttpMethod.Delete;
                case Operation.Upsert:
                    return HttpMethod.Put;
                case Operation.UpsertIncremental:
                    return HttpMethod.Patch;
                default:
                    throw new DataApiBuilderException(
                        message: "Operation not supported for the request.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }
        }

        /// <summary>
        /// Helper function handles the loading of the runtime config.
        /// </summary>
        public static RuntimeConfig SetupRuntimeConfig(string databaseEngine)
        {
            RuntimeConfigPath configPath = TestHelper.GetRuntimeConfigPath(databaseEngine);
            return TestHelper.GetRuntimeConfig(TestHelper.GetRuntimeConfigProvider(configPath));
        }

        /// <summary>
        /// For testing we use a JSON string that represents
        /// the runtime config that would otherwise be generated
        /// by the client for use by the runtime. This makes it
        /// easier to test with different configurations, and allows
        /// for different formats between database types.
        /// </summary>
        /// <param name="dbType"> the database type associated with this config.</param>
        /// <returns></returns>
        public static string GetRuntimeConfigJsonString(string dbType)
        {
            string magazinesSource = string.Empty;
            switch (dbType)
            {
                case TestCategory.MSSQL:
                case TestCategory.POSTGRESQL:
                    magazinesSource = "\"foo.magazines\"";
                    break;
                case TestCategory.MYSQL:
                    magazinesSource = "\"magazines\"";
                    break;
            }

            return
@"
{
  ""$schema"": ""../../project-dab/playground/dab.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": """ + dbType.ToLower() + @""",
    ""connection-string"": """"
  },
  """ + dbType.ToLower() + @""": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""enabled"": true,
      ""path"": ""/graphql"",
      ""allow-introspection"": true
    },
    ""host"": {
      ""mode"": ""Development"",
      ""cors"": {
      ""origins"": [ ""1"", ""2"" ],
      ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": """",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuer-key"": """"
        }
      }
    }
  },
  ""entities"": {
    ""Publisher"": {
      ""source"": ""publishers"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""update"", ""delete"" ]
        }
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""books""
        }
      }
    },
    ""Stock"": {
      ""source"": ""stocks"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""update"" ]
        }
      ],
      ""relationships"": {
        ""comics"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""comics"",
          ""source.fields"": [ ""categoryName"" ],
          ""target.fields"": [ ""categoryName"" ]
        }
      }
    },
    ""Book"": {
      ""source"": ""books"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""update"", ""delete"" ]
        }
      ],
      ""relationships"": {
        ""publisher"": {
          ""cardinality"": ""one"",
          ""target.entity"": ""publisher""
        },
        ""websiteplacement"": {
          ""cardinality"": ""one"",
          ""target.entity"": ""book_website_placements""
        },
        ""reviews"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""reviews""
        },
        ""authors"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""authors"",
          ""linking.object"": ""book_author_link"",
          ""linking.source.fields"": [ ""book_id"" ],
          ""linking.target.fields"": [ ""author_id"" ]
        }
      }
    },
    ""BookWebsitePlacement"": {
      ""source"": ""book_website_placements"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [
            ""create"",
            ""update"",
            {
              ""action"": ""delete"",
              ""policy"": {
                ""database"": ""@claims.id eq @item.id""
              },
              ""fields"": {
                ""include"": [ ""*"" ],
                ""exclude"": [ ""id"" ]
              }
            }
          ]
        }
      ],
      ""relationships"": {
          ""book_website_placements"": {
            ""cardinality"": ""one"",
            ""target.entity"": ""books""
          }
        }
      },
    ""Author"": {
      ""source"": ""authors"",
      ""rest"": true,
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
          ""books"": {
            ""cardinality"": ""many"",
            ""target.entity"": ""books"",
            ""linking.object"": ""book_author_link""
         }
       }
     },
    ""Review"": {
      ""source"": ""reviews"",
      ""rest"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ],
      ""relationships"": {
         ""books"": {
           ""cardinality"": ""one"",
           ""target.entity"": ""books""
         }
       }
     },
    ""Magazine"": {
      ""source"": " + magazinesSource + @",
      ""graphql"": true,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [
             {
             ""action"": ""*"",
             ""fields"": {
               ""include"": [ ""*"" ],
               ""exclude"": [ ""issue_number"" ]
              }
            }
          ]
        }
      ]
    },
    ""Comic"": {
      ""source"": ""comics"",
      ""rest"": true,
      ""graphql"": false,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        },
        {
          ""role"": ""authenticated"",
          ""actions"": [ ""create"", ""read"", ""delete"" ]
        }
      ]
    },
    ""Broker"": {
      ""source"": ""brokers"",
      ""graphql"": false,
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""read"" ]
        }
      ]
    },
    ""WebsiteUser"": {
      ""source"": ""website_users"",
      ""rest"": false,
      ""permissions"" : []
    }
  }
}";
        }

    }
}