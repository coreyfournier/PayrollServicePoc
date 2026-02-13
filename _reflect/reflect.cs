using System;
using System.Linq;
using System.Reflection;

var dllPath = @"C:\Users\CoreyFournier\.nuget\packages\anthropic.sdk\4.7.2\lib\net8.0\Anthropic.SDK.dll";
var asm = Assembly.LoadFrom(dllPath);

string FriendlyTypeName(Type t)
{
    if (t == null) return "null";
    if (t == typeof(string)) return "string";
    if (t == typeof(int)) return "int";
    if (t == typeof(bool)) return "bool";
    if (t == typeof(void)) return "void";
    if (t == typeof(object)) return "object";
    if (t.IsGenericType)
    {
        var name = t.Name.Split('`')[0];
        var args = string.Join(", ", t.GetGenericArguments().Select(FriendlyTypeName));
        return $"{name}<{args}>";
    }
    if (t.IsArray)
        return FriendlyTypeName(t.GetElementType()!) + "[]";
    return t.Name;
}

// MessageCountTokenParameters
Console.WriteLine("=== MessageCountTokenParameters ===");
var mctp = asm.GetType("Anthropic.SDK.Messaging.MessageCountTokenParameters");
if (mctp != null)
{
    Console.WriteLine($"  Base: {mctp.BaseType?.FullName ?? "none"}");
    foreach (var p in mctp.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var jsonAttr = p.GetCustomAttributes(false)
            .Where(a => a.GetType().Name == "JsonPropertyNameAttribute")
            .Select(a => a.GetType().GetProperty("Name")?.GetValue(a))
            .FirstOrDefault();
        var jsonName = jsonAttr != null ? $" [json: \"{jsonAttr}\"]" : "";
        Console.WriteLine($"  {FriendlyTypeName(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}{jsonName}");
    }
}

// MessageParameters declared-only properties
Console.WriteLine("\n=== MessageParameters (declared only) ===");
var msgParams = asm.GetType("Anthropic.SDK.Messaging.MessageParameters");
if (msgParams != null)
{
    Console.WriteLine($"  Base: {msgParams.BaseType?.FullName ?? "none"}");
    foreach (var p in msgParams.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var jsonAttr = p.GetCustomAttributes(false)
            .Where(a => a.GetType().Name == "JsonPropertyNameAttribute")
            .Select(a => a.GetType().GetProperty("Name")?.GetValue(a))
            .FirstOrDefault();
        var jsonIgnore = p.GetCustomAttributes(false).Any(a => a.GetType().Name == "JsonIgnoreAttribute");
        var jsonName = jsonAttr != null ? $" [json: \"{jsonAttr}\"]" : "";
        if (jsonIgnore) jsonName += " [JsonIgnore]";
        Console.WriteLine($"  {FriendlyTypeName(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}{jsonName}");
    }
}

// Check what IList<Tool> actually refers to (Messaging.Tool or Common.Tool)
Console.WriteLine("\n=== MessageParameters.Tools property type details ===");
if (msgParams != null)
{
    var toolsProp = msgParams.GetProperty("Tools");
    if (toolsProp != null)
    {
        var toolsType = toolsProp.PropertyType;
        Console.WriteLine($"  PropertyType: {toolsType.FullName}");
        if (toolsType.IsGenericType)
        {
            var genArgs = toolsType.GetGenericArguments();
            foreach (var g in genArgs)
                Console.WriteLine($"    GenericArg: {g.FullName}");
        }
    }
}

// Check the MessageParameters.Tools JSON serialization attribute
Console.WriteLine("\n=== MessageParameters.Tools all attributes ===");
if (msgParams != null)
{
    var toolsProp = msgParams.GetProperty("Tools");
    if (toolsProp != null)
    {
        foreach (var attr in toolsProp.GetCustomAttributes(false))
        {
            Console.WriteLine($"  Attr: {attr.GetType().FullName}");
            foreach (var ap in attr.GetType().GetProperties())
            {
                try { Console.WriteLine($"    {ap.Name} = {ap.GetValue(attr)}"); } catch { }
            }
        }
    }
}

// Check CacheControlType enum
Console.WriteLine("\n=== CacheControlType enum ===");
var cct = asm.GetType("Anthropic.SDK.Messaging.CacheControlType");
if (cct != null)
{
    foreach (var val in Enum.GetNames(cct))
        Console.WriteLine($"  {val} = {(int)Enum.Parse(cct, val)}");
}

// Check ContentBase more thoroughly
Console.WriteLine("\n=== ContentBase abstract members ===");
var cb = asm.GetType("Anthropic.SDK.Messaging.ContentBase");
if (cb != null)
{
    Console.WriteLine($"  IsAbstract: {cb.IsAbstract}");
    foreach (var p in cb.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        Console.WriteLine($"  {FriendlyTypeName(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}  IsAbstract={p.GetMethod?.IsAbstract}");
    }
    // Check custom attributes on ContentBase for JsonPolymorphic etc
    foreach (var attr in cb.GetCustomAttributes(false))
    {
        Console.WriteLine($"  ClassAttr: {attr.GetType().FullName}");
        foreach (var ap in attr.GetType().GetProperties())
        {
            try { Console.WriteLine($"    {ap.Name} = {ap.GetValue(attr)}"); } catch { }
        }
    }
}

// Check Function details more carefully - especially what the Function(name, type, additionalData) ctor does
Console.WriteLine("\n=== Function constructors with parameter defaults ===");
var func = asm.GetType("Anthropic.SDK.Common.Function");
if (func != null)
{
    var ctors = func.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
    foreach (var c in ctors)
    {
        var parms = c.GetParameters();
        var parmStr = string.Join(", ", parms.Select(p => {
            var def = p.HasDefaultValue ? $" = {p.DefaultValue ?? "null"}" : "";
            return $"{FriendlyTypeName(p.ParameterType)} {p.Name}{def}";
        }));
        Console.WriteLine($"  public .ctor({parmStr})");
    }
}

// Check Delta type
Console.WriteLine("\n=== Delta ===");
var delta = asm.GetType("Anthropic.SDK.Messaging.Delta");
if (delta != null)
{
    foreach (var p in delta.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        var jsonAttr = p.GetCustomAttributes(false)
            .Where(a => a.GetType().Name == "JsonPropertyNameAttribute")
            .Select(a => a.GetType().GetProperty("Name")?.GetValue(a))
            .FirstOrDefault();
        var jsonName = jsonAttr != null ? $" [json: \"{jsonAttr}\"]" : "";
        Console.WriteLine($"  {FriendlyTypeName(p.PropertyType)} {p.Name}{jsonName}");
    }
}

// Check ContentBlock type
Console.WriteLine("\n=== ContentBlock ===");
var contentBlock = asm.GetType("Anthropic.SDK.Messaging.ContentBlock");
if (contentBlock != null)
{
    foreach (var p in contentBlock.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        var jsonAttr = p.GetCustomAttributes(false)
            .Where(a => a.GetType().Name == "JsonPropertyNameAttribute")
            .Select(a => a.GetType().GetProperty("Name")?.GetValue(a))
            .FirstOrDefault();
        var jsonName = jsonAttr != null ? $" [json: \"{jsonAttr}\"]" : "";
        Console.WriteLine($"  {FriendlyTypeName(p.PropertyType)} {p.Name}{jsonName}");
    }
}

// Check Extensions class methods
Console.WriteLine("\n=== Extensions class methods ===");
var ext = asm.GetType("Anthropic.SDK.Messaging.Extensions");
if (ext != null)
{
    var methods = ext.GetMethods(BindingFlags.Public | BindingFlags.Static);
    foreach (var m in methods)
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{FriendlyTypeName(p.ParameterType)} {p.Name}"));
        Console.WriteLine($"  static {FriendlyTypeName(m.ReturnType)} {m.Name}({parms})");
    }
}
