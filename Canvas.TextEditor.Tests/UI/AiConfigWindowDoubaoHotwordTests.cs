using System.Reflection;
using ImageColorChanger.UI;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class AiConfigWindowDoubaoHotwordTests
    {
        [Fact]
        public void ParseDoubaoBoostingTableList_WithValidResponse_ShouldReturnTables()
        {
            string json = """
                          {
                            "ResponseMetadata": { "RequestId": "req-1" },
                            "Result": {
                              "BoostingTables": [
                                { "BoostingTableID": "tb_1", "BoostingTableName": "主日词表" },
                                { "BoostingTableID": "tb_2", "BoostingTableName": "经文词表" }
                              ]
                            }
                          }
                          """;

            object parseResult = InvokeParse(json);
            object tables = parseResult.GetType().GetProperty("Tables", BindingFlags.Public | BindingFlags.Instance)?.GetValue(parseResult);
            Assert.NotNull(tables);

            var tableList = Assert.IsAssignableFrom<System.Collections.IEnumerable>(tables);
            var items = new System.Collections.Generic.List<object>();
            foreach (object item in tableList)
            {
                items.Add(item);
            }

            Assert.Equal(2, items.Count);
            string firstId = items[0].GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(items[0])?.ToString();
            string firstName = items[0].GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(items[0])?.ToString();
            Assert.Equal("tb_1", firstId);
            Assert.Equal("主日词表", firstName);
        }

        [Fact]
        public void ParseDoubaoBoostingTableList_WithApiError_ShouldReturnErrorMessage()
        {
            string json = """
                          {
                            "ResponseMetadata": {
                              "Error": {
                                "Code": "InvalidParameter",
                                "Message": "bad request"
                              }
                            }
                          }
                          """;

            object parseResult = InvokeParse(json);
            string error = parseResult.GetType().GetProperty("ErrorMessage", BindingFlags.Public | BindingFlags.Instance)?.GetValue(parseResult)?.ToString();
            Assert.Contains("InvalidParameter", error);
            Assert.Contains("bad request", error);
        }

        private static object InvokeParse(string json)
        {
            var method = typeof(AiConfigWindow).GetMethod(
                "ParseDoubaoBoostingTableList",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            object result = method.Invoke(null, new object[] { json });
            Assert.NotNull(result);
            return result;
        }
    }
}

