namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

internal static class CSharpIdentifier
{
    private static readonly HashSet<string> s_reservedKeywords = new(StringComparer.Ordinal)
    {
        "__arglist", "__makeref", "__reftype", "__refvalue",
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    internal static string Escape(string name, bool isAsync) =>
        s_reservedKeywords.Contains(name) || (isAsync && name == "await") ? "@" + name : name;
}
