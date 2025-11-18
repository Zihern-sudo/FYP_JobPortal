using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace JobPortal.Areas.Shared.AI
{
    internal static class ParsedResumeSchema
    {
        // Shape-only, open-vocabulary. We never validate *content*, only types.
        private static readonly string Raw = """
        {
          "$schema":"https://json-schema.org/draft/2020-12/schema",
          "type":"object",
          "required":["basics","summary","skills","experience","education","certs","languages","meta"],
          "properties":{
            "basics":{
              "type":"object",
              "required":["fullName","email","phone","location"],
              "properties":{
                "fullName":{"type":"string"},
                "email":{"type":"string"},
                "phone":{"type":"string"},
                "location":{"type":"string"}
              },
              "additionalProperties":false
            },
            "summary":{"type":"string"},
            "skills":{
              "type":"object",
              "required":["hard","soft"],
              "properties":{
                "hard":{"type":"array","items":{"type":"string"}},
                "soft":{"type":"array","items":{"type":"string"}}
              },
              "additionalProperties":false
            },
            "experience":{
              "type":"array",
              "items":{
                "type":"object",
                "required":["title","company","start","end","years","highlights"],
                "properties":{
                  "title":{"type":"string"},
                  "company":{"type":"string"},
                  "start":{"type":"string"},
                  "end":{"type":"string"},
                  "years":{"type":"number"},
                  "highlights":{"type":"array","items":{"type":"string"}}
                },
                "additionalProperties":false
              }
            },
            "education":{
              "type":"array",
              "items":{
                "type":"object",
                "required":["degree","field","school"],
                "properties":{
                  "degree":{"type":"string"},
                  "field":{"type":"string"},
                  "school":{"type":"string"},
                  "year":{"type":"string"}
                },
                "additionalProperties":false
              }
            },
            "certs":{"type":"array","items":{"type":"string"}},
            "languages":{"type":"array","items":{"type":"string"}},
            "meta":{
              "type":"object",
              "required":["language","charCount","truncated"],
              "properties":{
                "language":{"type":"string"},
                "charCount":{"type":"number"},
                "truncated":{"type":"boolean"}
              },
              "additionalProperties":true
            }
          },
          "additionalProperties":true
        }
        """;

        private static JsonSchema? _compiled;
        public static JsonSchema Instance => _compiled ??= JsonSchema.FromText(Raw);

        public static bool IsValidJson(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node is null) return false;
                var result = Instance.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
                return result.IsValid;
            }
            catch { return false; }
        }
    }
}