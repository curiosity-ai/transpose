namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// The shapes the Curiosity front-end actually puts on the wire, run through both packages.
///
/// These are the types that exist *because* a Transpose JSON binding has no converter registry: a
/// wrapper whose runtime representation is a plain JavaScript string, reached only through an
/// implicit/explicit conversion operator (<c>UID128</c>, <c>LanguageDTO</c>). Nothing about them is
/// expressible in a native System.Text.Json snippet — they are <c>extern</c> + <c>[Template]</c> — so
/// the only meaningful oracle is the package they are migrating away from.
/// </summary>
[TestClass]
public sealed class MosaikShapeTests : CrossPackageTestBase
{
    // A faithful reduction of FrontEnd/Mosaik.FrontEnd.Core/src/Schema/Graph/UID128.cs: a class whose
    // values are plain strings at runtime, with an explicit string -> UID128 and an implicit
    // UID128 -> string operator.
    private const string Uid128 = """
        using System;
        using System.Collections.Generic;
        using Transpose;
        #USINGS#

        namespace UID
        {
            public sealed class UID128
            {
                [Template("UID.UID128.ThrowIfInvalid({value})")]
                public extern UID128(string value);

                private static string ThrowIfInvalid(string value)
                    => LooksValid(value) ? value : throw new ArgumentException("Invalid string content specified for UID128: '" + value + "'");

                public extern string Value { [Template("{this}")] get; }

                public static explicit operator UID128(string value) => string.IsNullOrEmpty(value) ? null : new UID128(value);
                public static implicit operator string(UID128 value) => value?.Value;

                [Template("Transpose.getHashCode({this})")]
                public extern override int GetHashCode();

                [Template("Transpose.equals({this}, {o})")]
                public extern override bool Equals(object o);

                public static bool LooksValid(string value) => value != null && value.Length == 22;

                public static UID128 Parse(string val) => new UID128(val);
            }
        }
        """;

    // A faithful reduction of FrontEnd/Mosaik.FrontEnd.Core/src/Schema/NLP/LanguageDTO.cs: a *struct*
    // that is its code string at runtime, so a Language reaches the server as "de" rather than as the
    // enum's number.
    private const string LanguageDto = """
        using System;
        using Transpose;
        #USINGS#

        namespace Mosaik.Schema
        {
            public enum Language { Any = 0, English = 1, German = 2, Portuguese = 3 }

            public static class Languages
            {
                public static string EnumToCode(Language value)
                {
                    switch (value)
                    {
                        case Language.English:    return "en";
                        case Language.German:     return "de";
                        case Language.Portuguese: return "pt";
                        default:                  return "--";
                    }
                }

                public static Language CodeToEnum(string code)
                {
                    switch (code)
                    {
                        case "en": return Language.English;
                        case "de": return Language.German;
                        case "pt": return Language.Portuguese;
                        default:   return Language.Any;
                    }
                }
            }

            public struct LanguageDTO : IEquatable<LanguageDTO>
            {
                [Template("Mosaik.Schema.LanguageDTO.Normalize({value})")]
                public extern LanguageDTO(string value);

                public static implicit operator LanguageDTO(Language value) => new LanguageDTO(Languages.EnumToCode(value));
                public static implicit operator Language(LanguageDTO value) => Languages.CodeToEnum(CodeOf(value));
                public static explicit operator LanguageDTO(string languageCode) => new LanguageDTO(languageCode);

                public static bool operator ==(LanguageDTO x, LanguageDTO y) => CodeOf(x) == CodeOf(y);
                public static bool operator !=(LanguageDTO x, LanguageDTO y) => CodeOf(x) != CodeOf(y);

                public bool Equals(LanguageDTO other) => this == other;

                [Template("{this} === {o}")]
                public extern override bool Equals(object o);

                [Template("Transpose.getHashCode(Mosaik.Schema.LanguageDTO.CodeOf({this}))")]
                public extern override int GetHashCode();

                private static string CodeOf(LanguageDTO value)
                    => Script.Write<string>("(typeof {0} === \"string\" ? {0} : null)", value) ?? Languages.EnumToCode(Language.Any);

                private static string Normalize(string languageCode)
                    => Languages.EnumToCode(string.IsNullOrWhiteSpace(languageCode) ? Language.English : Languages.CodeToEnum(languageCode));
            }
        }
        """;

    // =============================================================================================
    // UID128
    // =============================================================================================

    [TestMethod]
    public async Task AUid128MemberIsWrittenAsItsBareString() => await AssertSame(Uid128 + """

        public class Node
        {
            public UID.UID128 Uid    { get; set; }
            public UID.UID128 Parent { get; set; }
            public string     Label  { get; set; }
        }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(Json.Write(new Node
                {
                    Uid    = UID.UID128.Parse("1234567890123456789012"),
                    Parent = null,
                    Label  = "a node"
                }));
        }
        """);

    [TestMethod]
    public async Task AUid128MemberIsReadThroughItsExplicitOperator() => await AssertSame(Uid128 + """

        public class Node { public UID.UID128 Uid { get; set; } public string Label { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var node = Json.Read<Node>("{\"Uid\":\"1234567890123456789012\",\"Label\":\"a node\"}");

                Console.WriteLine(node.Uid.Value);
                Console.WriteLine(node.Uid == UID.UID128.Parse("1234567890123456789012"));
                Console.WriteLine(node.Label);
            }
        }
        """);

    [TestMethod]
    public async Task AUid128RoundTrips() => await AssertSame(Uid128 + """

        public class Node { public UID.UID128 Uid { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var original = new Node { Uid = UID.UID128.Parse("1234567890123456789012") };
                var json     = Json.Write(original);
                var back     = Json.Read<Node>(json);

                Console.WriteLine(json);
                Console.WriteLine(Json.Write(back) == json);
                Console.WriteLine(back.Uid.Equals(original.Uid));
            }
        }
        """);

    [TestMethod]
    public async Task AnEmptyStringBecomesANullUid128() => await AssertSame(Uid128 + """

        public class Node { public UID.UID128 Uid { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                // The explicit operator maps "" to null on purpose: some payloads carry a blank string
                // rather than null for "no value".
                Console.WriteLine(Json.Read<Node>("{\"Uid\":\"\"}").Uid == null ? "<null>" : "value");
                Console.WriteLine(Json.Read<Node>("{\"Uid\":null}").Uid == null ? "<null>" : "value");
                Console.WriteLine(Json.Read<Node>("{}").Uid == null ? "<null>" : "value");
            }
        }
        """);

    [TestMethod]
    public async Task Uid128sInCollectionsAndAsDictionaryKeys() => await AssertSame(Uid128 + """

        public class Bag
        {
            public UID.UID128[]                       Ids  { get; set; }
            public List<UID.UID128>                   More { get; set; }
            public Dictionary<UID.UID128, string>     Map  { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var a = UID.UID128.Parse("1111111111111111111111");
                var b = UID.UID128.Parse("2222222222222222222222");

                var bag = new Bag
                {
                    Ids  = new[] { a, b },
                    More = new List<UID.UID128> { a },
                    Map  = new Dictionary<UID.UID128, string> { [a] = "first" }
                };

                var json = Json.Write(bag);
                Console.WriteLine(json);

                var back = Json.Read<Bag>(json);
                Console.WriteLine(back.Ids.Length + "/" + back.Ids[1].Value + "/" + back.More[0].Value + "/" + back.Map.Count);
            }
        }
        """);

    // =============================================================================================
    // LanguageDTO
    // =============================================================================================

    [TestMethod]
    public async Task ALanguageDtoMemberIsWrittenAsItsCodeString() => await AssertSame(LanguageDto + """

        public class Request
        {
            public Mosaik.Schema.LanguageDTO Language { get; set; }
            public string                    Query    { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Json.Write(new Request { Language = Mosaik.Schema.Language.German, Query = "q" }));
                Console.WriteLine(Json.Write(new Request { Language = Mosaik.Schema.Language.Any,    Query = "q" }));
            }
        }
        """);

    [TestMethod]
    public async Task ALanguageDtoIsReadBackThroughItsOperator() => await AssertSame(LanguageDto + """

        public class Request { public Mosaik.Schema.LanguageDTO Language { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var request = Json.Read<Request>("{\"Language\":\"pt\"}");

                Mosaik.Schema.Language asEnum = request.Language;
                Console.WriteLine(asEnum);
                Console.WriteLine(request.Language == (Mosaik.Schema.LanguageDTO)Mosaik.Schema.Language.Portuguese);
            }
        }
        """);

    [TestMethod]
    public async Task ALanguageDtoRoundTrips() => await AssertSame(LanguageDto + """

        public class Request { public Mosaik.Schema.LanguageDTO Language { get; set; } public int Take { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new Request { Language = Mosaik.Schema.Language.English, Take = 10 });
                var back = Json.Read<Request>(json);

                Console.WriteLine(json);
                Console.WriteLine(Json.Write(back) == json);
            }
        }
        """);

    [TestMethod]
    public async Task AnUnassignedLanguageDtoIsWrittenAsAnEmptyObject() => await AssertSame(LanguageDto + """

        public class Request { public Mosaik.Schema.LanguageDTO Language { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                // The documented gap in LanguageDTO: default(LanguageDTO) never ran the constructor, so
                // it is an empty object rather than a code string. Both packages write it the same way,
                // which is what matters for the migration.
                Console.WriteLine(Json.Write(new Request()));
            }
        }
        """);

    // =============================================================================================
    // Enums, the way the front-end DTOs use them
    // =============================================================================================

    [TestMethod]
    public async Task EnumMembersOfEveryShape() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public enum SortMode  { Relevance = 0, DateAscending = 1, DateDescending = 2 }
        public enum IndexType { None = 0, Text = 1, Vector = 2 }

        public class SearchRequest
        {
            public SortMode              Sort      { get; set; }
            public SortMode?             Fallback  { get; set; }
            public SortMode?             Unset     { get; set; }
            public IndexType[]           Indexes   { get; set; }
            public List<SortMode>        Order     { get; set; }
            public Dictionary<IndexType, int> Weights { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var request = new SearchRequest
                {
                    Sort     = SortMode.DateDescending,
                    Fallback = SortMode.Relevance,
                    Indexes  = new[] { IndexType.Text, IndexType.Vector },
                    Order    = new List<SortMode> { SortMode.DateAscending },
                    Weights  = new Dictionary<IndexType, int> { [IndexType.Vector] = 3 }
                };

                var json = Json.Write(request);
                Console.WriteLine(json);

                var back = Json.Read<SearchRequest>(json);
                Console.WriteLine(back.Sort + "/" + back.Fallback + "/" + (back.Unset.HasValue ? "set" : "unset") + "/" + back.Indexes[1] + "/" + back.Order[0] + "/" + back.Weights[IndexType.Vector]);
            }
        }
        """);

    [TestMethod]
    public async Task AnEnumArrivesFromTheServerAsItsName() => await AssertSame("""
        using System;
        #USINGS#

        public enum ScheduledTaskType { Import = 0, Reindex = 1, Cleanup = 2 }

        public class Task { public ScheduledTaskType Type { get; set; } public ScheduledTaskType? Optional { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                // The Curiosity server registers a JsonStringEnumConverter, so an enum reaches the
                // browser as its name. Both packages accept that as well as the numeric form.
                var byName   = Json.Read<Task>("{\"Type\":\"Reindex\",\"Optional\":\"Cleanup\"}");
                var byNumber = Json.Read<Task>("{\"Type\":1,\"Optional\":2}");

                Console.WriteLine(byName.Type + "/" + byName.Optional);
                Console.WriteLine(byNumber.Type + "/" + byNumber.Optional);
            }
        }
        """);

    [TestMethod]
    public async Task AnEnumWithExplicitValuesAndGaps() => await AssertSame("""
        using System;
        #USINGS#

        public enum Status { Unknown = 0, Queued = 10, Running = 20, Done = 30 }

        public class T { public Status S { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Json.Write(new T { S = Status.Running }));
                Console.WriteLine(Json.Read<T>("{\"S\":30}").S);
                Console.WriteLine((int)Json.Read<T>("{\"S\":99}").S);
            }
        }
        """);

    [TestMethod]
    public async Task AFlagsEnum() => await AssertSame("""
        using System;
        #USINGS#

        [Flags]
        public enum Access { None = 0, Read = 1, Write = 2, Admin = 4 }

        public class T { public Access A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new T { A = Access.Read | Access.Admin });
                Console.WriteLine(json);
                Console.WriteLine(Json.Read<T>(json).A);
            }
        }
        """);

    // =============================================================================================
    // Attribute handling, on the shapes the shared types use
    // =============================================================================================

    [TestMethod]
    public async Task ShortenedMemberNamesAsTheSharedTypesUse() => await AssertSame("""
        using System;
        #USINGS#

        // Shared/Mosaik.Shared/SharedTypes.cs and Mosaik/Core.Shared/src/ExtractorStatus.cs rename most
        // members to one- or two-character wire names.
        public class ExtractorProgress
        {
            [#PROP("S")]   public string Stage                { get; set; }
            [#PROP("W")]   public string SubStage             { get; set; }
            [#PROP("P")]   public float  CurrentStageProgress { get; set; }
            [#PROP("PID")] public int    ProcessId            { get; set; }
            [#PROP("Re")]  public int?   RequestID            { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new ExtractorProgress { Stage = "extract", SubStage = "ocr", CurrentStageProgress = 0.5f, ProcessId = 42 });
                Console.WriteLine(json);

                var back = Json.Read<ExtractorProgress>(json);
                Console.WriteLine(back.Stage + "/" + back.SubStage + "/" + back.CurrentStageProgress + "/" + back.ProcessId + "/" + (back.RequestID.HasValue ? "set" : "unset"));
            }
        }
        """);

    [TestMethod]
    public async Task ARenamedMemberInsideACollection() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public class KeyValue
        {
            [#PROP("K")] public string Key   { get; set; }
            [#PROP("V")] public string Value { get; set; }
        }

        public class Holder { public List<KeyValue> Pairs { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new Holder { Pairs = new List<KeyValue> { new KeyValue { Key = "a", Value = "1" }, new KeyValue { Key = "b", Value = "2" } } });
                Console.WriteLine(json);

                var back = Json.Read<Holder>(json);
                Console.WriteLine(back.Pairs.Count + "/" + back.Pairs[1].Key + "/" + back.Pairs[1].Value);
            }
        }
        """);

    [TestMethod]
    public async Task AJsonConstructorOnAGraphSchemaShape() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public class SchemaDefinition
        {
            [JsonConstructor]
            public SchemaDefinition(string name, List<string> fields)
            {
                Name   = name;
                Fields = fields ?? new List<string>();
            }

            public string       Name   { get; }
            public List<string> Fields { get; }
        }

        public static class Program
        {
            public static void Main()
            {
                var back = Json.Read<SchemaDefinition>("{\"Name\":\"Person\",\"Fields\":[\"Title\",\"Email\"]}");
                Console.WriteLine(back.Name + "/" + back.Fields.Count + "/" + back.Fields[1]);

                var empty = Json.Read<SchemaDefinition>("{\"Name\":\"Empty\"}");
                Console.WriteLine(empty.Name + "/" + empty.Fields.Count);
            }
        }
        """);

    [TestMethod]
    public async Task AnObjectLiteralTypeIsHandedBackAsTheParsedValue() => await AssertSame("""
        using System;
        using Transpose;
        #USINGS#

        // Request.cs marks its response shapes [ObjectLiteral]: they compile to direct property access
        // on the parsed object, so neither package may walk them member by member.
        [ObjectLiteral]
        public class ServerStatus
        {
            public string Version { get; set; }
            public bool   Ready   { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var status = Json.Read<ServerStatus>("{\"Version\":\"1.2.3\",\"Ready\":true}");
                Console.WriteLine(status.Version + "/" + status.Ready);
            }
        }
        """);

    [TestMethod]
    public async Task ADeeplyNestedSearchResultShape() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public enum FacetKind { Term = 0, Range = 1 }

        public class Facet
        {
            [#PROP("n")] public string    Name  { get; set; }
            [#PROP("k")] public FacetKind Kind  { get; set; }
            [#PROP("c")] public int       Count { get; set; }
        }

        public class Hit
        {
            [#PROP("u")] public string              Uid    { get; set; }
            [#PROP("t")] public string              Title  { get; set; }
            [#PROP("s")] public double              Score  { get; set; }
            [#PROP("f")] public Dictionary<string, string> Fields { get; set; }
        }

        public class SearchResult
        {
            public List<Hit>   Hits   { get; set; }
            public List<Facet> Facets { get; set; }
            public long        Total  { get; set; }
            public bool        More   { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var result = new SearchResult
                {
                    Total  = 12345678901L,
                    More   = true,
                    Hits   = new List<Hit>
                    {
                        new Hit { Uid = "1111111111111111111111", Title = "First", Score = 0.75, Fields = new Dictionary<string, string> { ["author"] = "Ada" } },
                        new Hit { Uid = "2222222222222222222222", Title = "Second", Score = 0.5, Fields = new Dictionary<string, string>() }
                    },
                    Facets = new List<Facet> { new Facet { Name = "type", Kind = FacetKind.Term, Count = 3 } }
                };

                var json = Json.Write(result);
                Console.WriteLine(json);

                var back = Json.Read<SearchResult>(json);
                Console.WriteLine(back.Hits.Count + "/" + back.Hits[0].Title + "/" + back.Hits[0].Fields["author"] + "/" + back.Facets[0].Kind + "/" + back.Total + "/" + back.More);
                Console.WriteLine(Json.Write(back) == json);
            }
        }
        """);
}
