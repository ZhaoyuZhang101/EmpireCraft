$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$helper = Get-Content (Join-Path $root 'Scripts/HelperFunc/SafeTypeDiscovery.cs') -Raw
$fixtures = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
public abstract class TestClaim { }
public class FirstClaim : TestClaim { }
public class ExtensionClaim : TestClaim { }
public abstract class AbstractClaim : TestClaim { }
public class GenericClaim<T> : TestClaim { }
public class BrokenParent { }
public class FakeAssembly : Assembly {
    public Type[] types;
    public Exception error;
    public override string FullName => "Fixture";
    public override Type[] GetTypes() { if (error != null) throw error; return types; }
}
public class LazyParentBase : TypeDelegator {
    public LazyParentBase() : base(typeof(TestClaim)) { }
    public override bool IsAssignableFrom(Type type) {
        if (type == typeof(BrokenParent)) throw new TypeLoadException("Missing EntityFramework parent");
        return typeof(TestClaim).IsAssignableFrom(type);
    }
}
public static class ScanSafetyTests {
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    public static int Run() {
        var warnings = new List<string>();
        var valid = new FakeAssembly { types = new[] { typeof(FirstClaim), typeof(AbstractClaim), typeof(GenericClaim<>), typeof(string), null } };
        var extension = new FakeAssembly { types = new[] { typeof(ExtensionClaim) } };
        var result = SafeTypeDiscovery.GetConcreteDerivedTypes(typeof(TestClaim), new[] { valid, extension }, warnings.Add).ToArray();
        Check(result.SequenceEqual(new[] { typeof(FirstClaim), typeof(ExtensionClaim) }), "Keep own and third-party concrete claims; skip abstract/open generic/null/unrelated");
        Check(warnings.Count == 0, "Healthy scan has no warning");
        var partial = new FakeAssembly { error = new ReflectionTypeLoadException(new[] { typeof(FirstClaim), null }, new Exception[] { new FileNotFoundException() }) };
        result = SafeTypeDiscovery.GetConcreteDerivedTypes(typeof(TestClaim), new[] { partial, extension }, warnings.Add).ToArray();
        Check(result.Length == 2 && result.Contains(typeof(FirstClaim)), "Partial load retains healthy types");
        var lazy = new FakeAssembly { types = new[] { typeof(BrokenParent), typeof(FirstClaim), typeof(BrokenParent) } };
        warnings.Clear();
        result = SafeTypeDiscovery.GetConcreteDerivedTypes(new LazyParentBase(), new[] { lazy, extension }, warnings.Add).ToArray();
        Check(result.SequenceEqual(new[] { typeof(FirstClaim), typeof(ExtensionClaim) }), "IsAssignableFrom failure does not abort claim initialization");
        Check(warnings.Count == 1, "One warning per assembly, not per broken type");
        foreach (var error in new Exception[] { new TypeLoadException(), new FileNotFoundException(), new FileLoadException(), new BadImageFormatException(), new NotSupportedException() }) {
            result = SafeTypeDiscovery.GetConcreteDerivedTypes(typeof(TestClaim), new[] { new FakeAssembly { error = error }, extension }, warnings.Add).ToArray();
            Check(result.Length == 1 && result[0] == typeof(ExtensionClaim), "Skip only unloadable assembly and continue");
        }
        bool unexpectedPropagated = false;
        try { SafeTypeDiscovery.GetConcreteDerivedTypes(typeof(TestClaim), new[] { new FakeAssembly { error = new InvalidOperationException() } }).ToArray(); }
        catch (InvalidOperationException) { unexpectedPropagated = true; }
        Check(unexpectedPropagated, "Unrelated programming errors are not swallowed");
        return passed;
    }
}
'@
Add-Type -TypeDefinition ($fixtures + [regex]::Replace($helper, '(?m)^using [^;]+;\s*', ''))
Write-Output "$([ScanSafetyTests]::Run()) type-discovery assertions passed."
foreach ($path in 'Scripts/Regimes/FixedFaction.cs', 'Scripts/HelperFunc/TemporaryFactionConverter.cs') {
    $source = Get-Content (Join-Path $root $path) -Raw
    if (-not $source.Contains('SafeTypeDiscovery.GetConcreteDerivedTypes(')) { throw "Unsafe discovery: $path" }
}
Write-Output '2 claim initialization/deserialization wiring checks passed.'
