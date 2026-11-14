// 替身层生成器：独立服务器不带世界盒子，模组核心源码照旧写着 Kingdom、City、Actor、Mathf 这些名字。
// 这个工具读模组编出来的 EmpireCraft.dll，找出搬进服务器的那部分代码用到了外部程序集(原版、Unity、加载器)的
// 哪些类型、哪些成员，照着名字和签名生成一份空壳源码：字段就是字段，属性是自动属性，方法体只记一笔"还没实现"。
// 只抄名字与签名，不抄任何实现；真正的实现在 Shim/ 下手写(partial 类补成员，或登记进 handwritten.txt 后整个接手)。
//
//   ShimGen <EmpireCraft.dll> <输出目录> <include.txt> <handwritten.txt> <参考程序集目录>...
//
// include.txt：一行一个命名空间桶(EmpireCraft.Scripts.<桶>)或 T:类型全名(桶外另外搬进来的类型)。
//   -T:类型全名 = 命名空间在桶里、文件却没搬进来的类型(当外部的出壳)；+T:类型全名 / +类型全名::成员名 = 硬加进壳的。
// handwritten.txt：一行一个手写接手的东西，T:类型全名(整个类型不生成) 或 类型全名::成员名(这个成员不生成)。
using System.Globalization;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 5)
{
    Console.Error.WriteLine("usage: ShimGen <EmpireCraft.dll> <outDir> <include.txt> <handwritten.txt> <refDir>...");
    return 2;
}

string dllPath = args[0], outDir = args[1];
var buckets = new HashSet<string>();
var extraTypes = new HashSet<string>();
var forced = new List<string>();
var excludedTypes = new HashSet<string>();
foreach (string raw in File.ReadAllLines(args[2]))
{
    string line = raw.Trim();
    if (line.Length == 0 || line[0] == '#') continue;
    if (line.StartsWith("new:")) Generator.AutoNewTypes.Add(line.Substring(4));
    else if (line.StartsWith("-T:")) excludedTypes.Add(line.Substring(3));
    else if (line.StartsWith("+")) forced.Add(line.Substring(1));
    else if (line.StartsWith("T:")) extraTypes.Add(line.Substring(2));
    else buckets.Add(line);
}
var handTypes = new HashSet<string>();
var handMembers = new HashSet<string>();
if (File.Exists(args[3]))
    foreach (string raw in File.ReadAllLines(args[3]))
    {
        string line = raw.Trim();
        if (line.Length == 0 || line[0] == '#') continue;
        if (line.StartsWith("T:")) handTypes.Add(line.Substring(2));
        else handMembers.Add(line);
    }

// 手写文件自己也能登记：一行 "//# hand 类型全名: 成员1 成员2 ..."(构造函数写 ctor)，或 "//# hand T:类型全名"(整个类型接手)
string shimRoot = Path.GetDirectoryName(Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
if (shimRoot != null && Directory.Exists(shimRoot))
    foreach (string file in Directory.GetFiles(shimRoot, "*.cs", SearchOption.AllDirectories))
    {
        if (file.EndsWith(".g.cs")) continue;
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim();
            if (!line.StartsWith("//# hand ")) continue;
            line = line.Substring(9).Trim();
            if (line.StartsWith("T:"))
            {
                handTypes.Add(line.Substring(2).Trim());
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string owner = line.Substring(0, colon).Trim();
            foreach (string member in line.Substring(colon + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries)) handMembers.Add(owner + "::" + member);
        }
    }

var resolver = new ShimResolver();
for (int i = 4; i < args.Length; i++) resolver.AddSearchDirectory(args[i]);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dllPath)));
var asm = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters { AssemblyResolver = resolver });
string self = asm.Name.Name;

bool Included(TypeDefinition type)
{
    while (type.DeclaringType != null) type = type.DeclaringType;
    if (excludedTypes.Contains(type.FullName)) return false;
    if (extraTypes.Contains(type.FullName)) return true;
    string[] parts = (type.Namespace ?? "").Split('.');
    string bucket = parts.Length >= 3 && parts[1] == "Scripts" ? parts[2] : parts.Length >= 2 ? parts[1] : "(root)";
    return buckets.Contains(bucket);
}

var gen = new Generator(self, handTypes, handMembers, Included);

var modules = new List<ModuleDefinition> { asm.MainModule };
foreach (AssemblyNameReference reference in asm.MainModule.AssemblyReferences)
{
    AssemblyDefinition resolved = resolver.Resolve(reference);
    if (resolved != null) modules.Add(resolved.MainModule);
}
gen.Modules = modules;
int scanned = 0;
foreach (TypeDefinition type in asm.MainModule.GetTypes())
{
    if (!Included(type)) continue;
    scanned++;
    gen.ScanModType(type);
}
// 补充清单：源码比这份 dll 新、或只在 nameof 里出现的东西，照名字硬加
foreach (string spec in forced)
    if (!gen.Force(modules, spec)) Console.WriteLine("  forced entry not found: " + spec);
gen.Close();
Directory.CreateDirectory(outDir);
foreach (string old in Directory.GetFiles(outDir, "*.g.cs")) File.Delete(old);
gen.Write(outDir);
Console.WriteLine($"scanned {scanned} mod types; shim: {gen.TypeCount} types, {gen.MemberCount} members; skipped {gen.Skipped.Count}");
foreach (string note in gen.Skipped.Take(40)) Console.WriteLine("  skipped: " + note);
return 0;

sealed class ShimResolver : DefaultAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> _cache = new();

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        if (_cache.TryGetValue(name.Name, out AssemblyDefinition hit)) return hit;
        AssemblyDefinition found = null;
        // 原版程序集优先用公开化过的那一份(模组就是对着它编的，成员可见性以它为准)
        if (name.Name == "Assembly-CSharp")
            foreach (string dir in GetSearchDirectories())
            {
                string publicized = Path.Combine(dir, "Assembly-CSharp-Publicized.dll");
                if (!File.Exists(publicized)) continue;
                found = AssemblyDefinition.ReadAssembly(publicized, new ReaderParameters { AssemblyResolver = this });
                break;
            }
        try
        {
            found ??= base.Resolve(name);
        }
        catch (AssemblyResolutionException)
        {
            found = null;
        }
        _cache[name.Name] = found;
        return found;
    }
}

sealed class Generator
{
    private readonly string _self;
    private readonly HashSet<string> _handTypes, _handMembers;
    // 要生成的外部类型 → 它被用到的成员
    private readonly Dictionary<TypeDefinition, HashSet<IMemberDefinition>> _types = new();
    private readonly Queue<TypeDefinition> _pending = new();
    private readonly Dictionary<IMemberDefinition, int> _refs = new();
    public readonly List<string> Skipped = new();
    public List<ModuleDefinition> Modules = new();
    // 模组的补丁(Harmony)按名字去找、要挂上去的成员：壳里必须有，且不许被内联(内联了补丁就挂不上)
    private readonly HashSet<IMemberDefinition> _patched = new();
    public int TypeCount => _types.Count;
    public int MemberCount => _types.Sum(pair => pair.Value.Count);

    private readonly Func<TypeDefinition, bool> _included;

    public Generator(string self, HashSet<string> handTypes, HashSet<string> handMembers, Func<TypeDefinition, bool> included)
    {
        _included = included;
        _self = self;
        _handTypes = handTypes;
        _handMembers = handMembers;
    }

    // ───────── 判定 ─────────
    private static string ScopeName(TypeReference type) => type.Scope switch
    {
        AssemblyNameReference reference => reference.Name,
        ModuleDefinition module => module.Assembly.Name.Name,
        _ => type.Scope?.Name ?? ""
    };

    private static bool IsFramework(string scope) =>
        scope == "mscorlib" || scope == "netstandard" || scope == "System" || scope.StartsWith("System.") || scope == "Microsoft.CSharp" ||
        scope == "Newtonsoft.Json" || scope == "0Harmony" || scope.StartsWith("Mono.") || scope.StartsWith("MonoMod");

    // 要出壳的类型：外部程序集(原版、Unity、加载器)的，和模组自己没搬进服务器的那些(界面、补丁、神力)
    private bool IsShim(TypeReference type)
    {
        if (type == null) return false;
        type = Root(type);
        if (type is GenericParameter) return false;
        string scope = ScopeName(type);
        if (IsFramework(scope)) return false;
        if (scope != _self) return true;
        try
        {
            TypeDefinition definition = type.Resolve();
            return definition != null && !_included(definition);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ───────── 收集 ─────────
    private TypeDefinition AddType(TypeReference reference)
    {
        if (reference == null) return null;
        switch (reference)
        {
            case GenericParameter:
                return null;
            case GenericInstanceType instance:
                foreach (TypeReference argument in instance.GenericArguments) AddType(argument);
                return AddType(instance.ElementType);
            case TypeSpecification specification:
                return AddType(specification.ElementType);
        }
        if (!IsShim(reference)) return null;
        TypeDefinition definition;
        try
        {
            definition = reference.Resolve();
        }
        catch (Exception)
        {
            definition = null;
        }
        if (definition == null)
        {
            Skip("unresolved type " + reference.FullName);
            return null;
        }
        if (_types.ContainsKey(definition)) return definition;
        _types[definition] = new HashSet<IMemberDefinition>();
        _pending.Enqueue(definition);
        return definition;
    }

    private void Skip(string note)
    {
        if (!Skipped.Contains(note)) Skipped.Add(note);
    }

    private void AddMember(IMemberDefinition member, int count = 1)
    {
        if (member == null) return;
        TypeDefinition owner = AddType(member.DeclaringType);
        if (owner == null) return;
        _refs[member] = _refs.GetValueOrDefault(member) + count;
        if (!_types[owner].Add(member)) return;
        switch (member)
        {
            case FieldDefinition field:
                AddType(field.FieldType);
                break;
            case PropertyDefinition property:
                AddType(property.PropertyType);
                foreach (ParameterDefinition parameter in property.Parameters) AddType(parameter.ParameterType);
                // 覆盖的虚属性：基类那一个也要在
                MethodDefinition accessor = property.GetMethod ?? property.SetMethod;
                if (accessor != null && accessor.IsVirtual && !accessor.IsNewSlot)
                {
                    MethodDefinition baseAccessor = BaseMethod(accessor);
                    if (baseAccessor != null) AddMember(PropertyOf(baseAccessor) ?? (IMemberDefinition)baseAccessor, 0);
                }
                break;
            case EventDefinition @event:
                AddType(@event.EventType);
                break;
            case MethodDefinition method:
                AddType(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters) AddType(parameter.ParameterType);
                foreach (GenericParameter generic in method.GenericParameters)
                foreach (GenericParameterConstraint constraint in generic.Constraints) AddType(constraint.ConstraintType);
                if (method.IsVirtual && !method.IsNewSlot)
                {
                    MethodDefinition baseMethod = BaseMethod(method);
                    if (baseMethod != null) AddMember(baseMethod, 0);
                }
                break;
        }
    }

    private static PropertyDefinition PropertyOf(MethodDefinition accessor)
    {
        if (accessor == null || !(accessor.IsGetter || accessor.IsSetter)) return null;
        foreach (PropertyDefinition property in accessor.DeclaringType.Properties)
            if (property.GetMethod == accessor || property.SetMethod == accessor) return property;
        return null;
    }

    private static EventDefinition EventOf(MethodDefinition accessor)
    {
        if (accessor == null || !(accessor.IsAddOn || accessor.IsRemoveOn)) return null;
        foreach (EventDefinition @event in accessor.DeclaringType.Events)
            if (@event.AddMethod == accessor || @event.RemoveMethod == accessor) return @event;
        return null;
    }

    // 沿基类链找被这个方法覆盖的那一个(同名、同参数个数与类型名)
    private static MethodDefinition BaseMethod(MethodDefinition method)
    {
        TypeReference baseReference = method.DeclaringType.BaseType;
        while (baseReference != null)
        {
            TypeDefinition baseType;
            try
            {
                baseType = baseReference.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
            if (baseType == null) return null;
            foreach (MethodDefinition candidate in baseType.Methods)
            {
                if (candidate.Name != method.Name || !candidate.IsVirtual || candidate.Parameters.Count != method.Parameters.Count ||
                    candidate.GenericParameters.Count != method.GenericParameters.Count) continue;
                bool same = true;
                for (int i = 0; i < candidate.Parameters.Count && same; i++)
                    same = candidate.Parameters[i].ParameterType is GenericParameter ||
                           candidate.Parameters[i].ParameterType.Name == method.Parameters[i].ParameterType.Name;
                if (same) return candidate;
            }
            baseReference = baseType.BaseType;
        }
        return null;
    }

    private void AddMethodReference(MethodReference reference)
    {
        if (reference == null) return;
        if (reference is GenericInstanceMethod instance)
            foreach (TypeReference argument in instance.GenericArguments) AddType(argument);
        AddType(reference.DeclaringType);
        if (!IsShim(reference.DeclaringType)) return;
        MethodDefinition definition;
        try
        {
            definition = reference.Resolve();
        }
        catch (Exception)
        {
            definition = null;
        }
        if (definition == null)
        {
            Skip("unresolved method " + reference.FullName);
            return;
        }
        AddMember(PropertyOf(definition) ?? EventOf(definition) ?? (IMemberDefinition)definition);
    }

    private static TypeReference Root(TypeReference type)
    {
        while (true)
        {
            if (type is GenericInstanceType instance) type = instance.ElementType;
            else if (type is TypeSpecification specification) type = specification.ElementType;
            else return type;
        }
    }

    private void AddFieldReference(FieldReference reference)
    {
        if (reference == null) return;
        AddType(reference.DeclaringType);
        if (!IsShim(reference.DeclaringType)) return;
        FieldDefinition definition;
        try
        {
            definition = reference.Resolve();
        }
        catch (Exception)
        {
            definition = null;
        }
        if (definition == null)
        {
            Skip("unresolved field " + reference.FullName);
            return;
        }
        AddMember(definition);
    }

    private void AddAttributes(ICustomAttributeProvider provider)
    {
        if (!provider.HasCustomAttributes) return;
        foreach (CustomAttribute attribute in provider.CustomAttributes)
        {
            AddMethodReference(attribute.Constructor);
            if (!IsShim(attribute.AttributeType)) continue;
            TypeDefinition type;
            try
            {
                type = attribute.AttributeType.Resolve();
            }
            catch (Exception)
            {
                continue;
            }
            if (type == null) continue;
            foreach (CustomAttributeNamedArgument named in attribute.Properties)
                AddMember(FindUp(type, t => t.Properties.FirstOrDefault(p => p.Name == named.Name)));
            foreach (CustomAttributeNamedArgument named in attribute.Fields)
                AddMember(FindUp(type, t => t.Fields.FirstOrDefault(f => f.Name == named.Name)));
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
                if (argument.Value is TypeReference typeArgument) AddType(typeArgument);
        }
    }

    private static IMemberDefinition FindUp(TypeDefinition type, Func<TypeDefinition, IMemberDefinition> pick)
    {
        while (type != null)
        {
            IMemberDefinition found = pick(type);
            if (found != null) return found;
            try
            {
                type = type.BaseType?.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    private static readonly HashSet<string> Lookups = new()
    {
        "Method", "DeclaredMethod", "PropertyGetter", "PropertySetter", "DeclaredPropertyGetter", "DeclaredPropertySetter", "Property", "DeclaredProperty",
        "Field", "DeclaredField", "Constructor", "DeclaredConstructor", "GetMethod", "GetField", "GetProperty", "FieldRefAccess"
    };

    private static bool IsLookup(MethodReference call) =>
        Lookups.Contains(call.Name) && (call.DeclaringType.FullName == "HarmonyLib.AccessTools" || call.DeclaringType.FullName == "System.Type");

    // 模组按名字找的成员(typeof(X) 加 "名字"，或 "X:名字")：同名的方法、属性、字段都放进壳，记作补丁目标
    private void PatchTarget(TypeReference owner, string name, bool constructor)
    {
        if (name != null && name.Contains(':') && !name.Contains(' '))
        {
            string typeName = name.Substring(0, name.IndexOf(':'));
            name = name.Substring(name.IndexOf(':') + 1).TrimStart(':');
            owner = null;
            foreach (ModuleDefinition module in Modules)
            {
                owner = module.GetType(typeName);
                if (owner != null) break;
            }
        }
        if (owner == null || !IsShim(owner)) return;
        TypeDefinition type;
        try
        {
            type = Root(owner).Resolve();
        }
        catch (Exception)
        {
            return;
        }
        if (type == null) return;
        for (TypeDefinition at = type; at != null;)
        {
            if (!IsShim(at)) break;
            var found = new List<IMemberDefinition>();
            if (constructor) found.AddRange(at.Methods.Where(m => m.IsConstructor && !m.IsStatic));
            else if (name != null)
            {
                foreach (MethodDefinition method in at.Methods.Where(m => m.Name == name))
                    found.Add(PropertyOf(method) ?? EventOf(method) ?? (IMemberDefinition)method);
                found.AddRange(at.Properties.Where(p => p.Name == name));
                found.AddRange(at.Fields.Where(f => f.Name == name));
            }
            foreach (IMemberDefinition member in found)
            {
                AddMember(member, 0);
                _patched.Add(member);
            }
            if (found.Count > 0 || constructor) break;
            try
            {
                at = at.BaseType?.Resolve();
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private static void ReadPatchAttributes(ICustomAttributeProvider provider, ref TypeReference owner, ref string name, ref bool constructor)
    {
        if (!provider.HasCustomAttributes) return;
        foreach (CustomAttribute attribute in provider.CustomAttributes)
        {
            if (attribute.AttributeType.Name != "HarmonyPatch") continue;
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            {
                if (argument.Value is TypeReference type) owner = type;
                else if (argument.Value is string text) name = text;
                // MethodType：1 Getter 2 Setter 3 Constructor
                else if (argument.Type.Name == "MethodType" && Convert.ToInt32(argument.Value) == 3) constructor = true;
            }
        }
    }

    // 照名字硬加：T:类型全名，或 类型全名::成员名(同名的都要)
    public bool Force(List<ModuleDefinition> modules, string spec)
    {
        bool typeOnly = spec.StartsWith("T:");
        string typeName = typeOnly ? spec.Substring(2) : spec.Substring(0, spec.IndexOf("::", StringComparison.Ordinal));
        string memberName = typeOnly ? null : spec.Substring(spec.IndexOf("::", StringComparison.Ordinal) + 2);
        TypeDefinition type = null;
        foreach (ModuleDefinition module in modules)
        {
            type = module.GetType(typeName) ?? module.GetType(typeName.Replace('+', '/'));
            if (type != null) break;
        }
        // 只给了短名的：各模块里按名字找头一个
        if (type == null && !typeName.Contains('.'))
            foreach (ModuleDefinition module in modules)
            {
                type = module.GetTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null) break;
            }
        if (type == null || AddType(type) == null) return false;
        if (typeOnly) return true;
        bool any = false;
        foreach (FieldDefinition field in type.Fields.Where(f => f.Name == memberName).ToList())
        {
            AddMember(field);
            any = true;
        }
        foreach (PropertyDefinition property in type.Properties.Where(p => p.Name == memberName).ToList())
        {
            AddMember(property);
            any = true;
        }
        foreach (MethodDefinition method in type.Methods.Where(m => m.Name == memberName && !m.IsGetter && !m.IsSetter).ToList())
        {
            AddMember(method);
            any = true;
        }
        return any;
    }

    // 模组自己的一个类型：它的基类、接口、签名、方法体里碰到的外部东西都记下
    public void ScanModType(TypeDefinition type)
    {
        AddType(type.BaseType);
        foreach (InterfaceImplementation implemented in type.Interfaces) AddType(implemented.InterfaceType);
        AddAttributes(type);
        foreach (GenericParameter generic in type.GenericParameters)
        foreach (GenericParameterConstraint constraint in generic.Constraints) AddType(constraint.ConstraintType);
        foreach (FieldDefinition field in type.Fields)
        {
            AddType(field.FieldType);
            AddAttributes(field);
        }
        foreach (PropertyDefinition property in type.Properties) AddAttributes(property);
        TypeReference classTarget = null;
        string className = null;
        bool classConstructor = false;
        ReadPatchAttributes(type, ref classTarget, ref className, ref classConstructor);
        foreach (MethodDefinition method in type.Methods)
        {
            TypeReference target = classTarget;
            string targetName = className;
            bool targetConstructor = classConstructor;
            ReadPatchAttributes(method, ref target, ref targetName, ref targetConstructor);
            if (target != null && (targetName != null || targetConstructor) && method.HasCustomAttributes) PatchTarget(target, targetName, targetConstructor);
            AddType(method.ReturnType);
            AddAttributes(method);
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                AddType(parameter.ParameterType);
                AddAttributes(parameter);
            }
            foreach (GenericParameter generic in method.GenericParameters)
            foreach (GenericParameterConstraint constraint in generic.Constraints) AddType(constraint.ConstraintType);
            foreach (MethodReference overridden in method.Overrides) AddMethodReference(overridden);
            // 覆盖外部基类的虚方法：那个虚方法得在壳里
            if (method.IsVirtual && !method.IsNewSlot)
            {
                MethodDefinition baseMethod = BaseMethod(method);
                if (baseMethod != null && IsShim(baseMethod.DeclaringType))
                    AddMember(PropertyOf(baseMethod) ?? (IMemberDefinition)baseMethod, 0);
            }
            if (!method.HasBody) continue;
            foreach (VariableDefinition variable in method.Body.Variables) AddType(variable.VariableType);
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers) AddType(handler.CatchType);
            TypeReference lastType = null, ownerAtString = null;
            string lastString = null;
            foreach (Instruction instruction in method.Body.Instructions)
                switch (instruction.Operand)
                {
                    case string text:
                        lastString = text;
                        ownerAtString = lastType;
                        break;
                    case MethodReference methodReference:
                        AddMethodReference(methodReference);
                        if (IsLookup(methodReference))
                        {
                            bool constructor = methodReference.Name.Contains("Constructor");
                            if (lastString != null || constructor) PatchTarget(lastString != null ? ownerAtString : lastType, lastString, constructor);
                            lastString = null;
                        }
                        break;
                    case FieldReference fieldReference:
                        AddFieldReference(fieldReference);
                        break;
                    case TypeReference typeReference:
                        AddType(typeReference);
                        if (instruction.OpCode == OpCodes.Ldtoken) lastType = typeReference;
                        break;
                }
        }
    }

    // 闭包：壳自己的基类、外层类型、泛型约束、枚举与委托的全部内容
    public void Close()
    {
        while (_pending.Count > 0)
        {
            TypeDefinition type = _pending.Dequeue();
            if (type.DeclaringType != null) AddType(type.DeclaringType);
            if (type.BaseType != null) AddType(type.BaseType);
            foreach (GenericParameter generic in type.GenericParameters)
            foreach (GenericParameterConstraint constraint in generic.Constraints) AddType(constraint.ConstraintType);
            if (type.IsEnum) continue;
            // 常量在模组编出来的代码里已经化成字面量、看不出引用：壳里的类型把自己的常量都带上
            foreach (FieldDefinition constant in type.Fields.Where(f => f.IsLiteral && f.HasConstant).ToList()) AddMember(constant, 0);
            if (IsDelegate(type))
            {
                MethodDefinition invoke = type.Methods.FirstOrDefault(m => m.Name == "Invoke");
                if (invoke != null)
                {
                    AddType(invoke.ReturnType);
                    foreach (ParameterDefinition parameter in invoke.Parameters) AddType(parameter.ParameterType);
                }
            }
        }
        // 接口：只留壳里有的那些；基接口同理(在输出时判断)
    }

    private static bool IsDelegate(TypeDefinition type) => type.BaseType?.FullName == "System.MulticastDelegate";

    // ───────── 输出 ─────────
    private static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
        "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while"
    };

    private static string Id(string name) => Keywords.Contains(name) ? "@" + name : name;

    private static string Bare(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }

    private static int Arity(TypeReference type)
    {
        int tick = type.Name.IndexOf('`');
        return tick < 0 ? 0 : int.Parse(type.Name.Substring(tick + 1), CultureInfo.InvariantCulture);
    }

    private bool Unsupported(TypeReference type)
    {
        switch (type)
        {
            case null:
                return false;
            case PointerType or FunctionPointerType or PinnedType:
                return true;
            case GenericInstanceType instance:
                return instance.GenericArguments.Any(Unsupported) || Unsupported(instance.ElementType);
            case TypeSpecification specification:
                return Unsupported(specification.ElementType);
            case GenericParameter:
                return false;
        }
        // 签名里出现了解析不了的外部类型：这个成员写不出来
        if (!IsShim(type)) return false;
        TypeDefinition definition;
        try
        {
            definition = type.Resolve();
        }
        catch (Exception)
        {
            return true;
        }
        return definition == null || !_types.ContainsKey(definition);
    }

    // 类型写成 C#：global::命名空间.外层<参>.内层<参>
    private string Cs(TypeReference type)
    {
        switch (type)
        {
            case GenericParameter parameter:
                return Id(parameter.Name);
            case ByReferenceType byReference:
                return Cs(byReference.ElementType);
            case ArrayType array:
                return Cs(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
            case RequiredModifierType required:
                return Cs(required.ElementType);
            case OptionalModifierType optional:
                return Cs(optional.ElementType);
            case GenericInstanceType instance:
                return Named(instance.ElementType, instance.GenericArguments.Select(Cs).ToList());
        }
        if (type.FullName == "System.Void") return "void";
        // 泛型定义本身(声明处、约束里)：用它自己的参数名
        return Named(type, type.HasGenericParameters ? type.GenericParameters.Select(p => Id(p.Name)).ToList() : null);
    }

    private static string Named(TypeReference type, List<string> arguments)
    {
        var chain = new List<TypeReference>();
        for (TypeReference at = type; at != null; at = at.DeclaringType) chain.Insert(0, at);
        var text = new StringBuilder("global::");
        if (!string.IsNullOrEmpty(chain[0].Namespace)) text.Append(chain[0].Namespace).Append('.');
        int used = 0;
        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0) text.Append('.');
            text.Append(Id(Bare(chain[i].Name)));
            int arity = Arity(chain[i]);
            if (arity <= 0) continue;
            text.Append('<');
            for (int k = 0; k < arity; k++)
            {
                if (k > 0) text.Append(k > 0 && arguments == null ? "" : ", ");
                if (arguments != null && used < arguments.Count) text.Append(arguments[used]);
                else if (arguments == null && k > 0) text.Append(',');
                used++;
            }
            text.Append('>');
        }
        return text.ToString();
    }

    private static string Literal(object value, TypeReference type, Func<TypeReference, string> cs)
    {
        TypeDefinition definition = null;
        try
        {
            definition = type is GenericParameter ? null : type.Resolve();
        }
        catch (Exception)
        {
            // 解析不了就按原样写
        }
        if (value == null) return definition != null && definition.IsValueType || type is GenericParameter ? "default" : "null";
        if (definition != null && definition.IsEnum) return "(" + cs(type) + ")(" + Convert.ToString(value, CultureInfo.InvariantCulture) + ")";
        switch (value)
        {
            case bool flag:
                return flag ? "true" : "false";
            case string text:
                return Quote(text);
            case char character:
                return "(char)" + (int)character;
            case float single:
                return float.IsNaN(single) ? "float.NaN" : float.IsPositiveInfinity(single) ? "float.PositiveInfinity" :
                    float.IsNegativeInfinity(single) ? "float.NegativeInfinity" : single.ToString("R", CultureInfo.InvariantCulture) + "f";
            case double number:
                return double.IsNaN(number) ? "double.NaN" : double.IsPositiveInfinity(number) ? "double.PositiveInfinity" :
                    double.IsNegativeInfinity(number) ? "double.NegativeInfinity" : number.ToString("R", CultureInfo.InvariantCulture) + "d";
            case long: return Convert.ToString(value, CultureInfo.InvariantCulture) + "L";
            case ulong: return Convert.ToString(value, CultureInfo.InvariantCulture) + "UL";
            case uint: return Convert.ToString(value, CultureInfo.InvariantCulture) + "U";
            default: return "(" + cs(type) + ")(" + Convert.ToString(value, CultureInfo.InvariantCulture) + ")";
        }
    }

    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in text)
            quoted.Append(c switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\0' => "\\0",
                _ => c < ' ' ? "\\u" + ((int)c).ToString("x4") : c.ToString()
            });
        return quoted.Append('"').ToString();
    }

    private bool Hand(TypeDefinition type, string member) => _handMembers.Contains(type.FullName.Replace('/', '.') + "::" + member);

    private string Generics(IGenericParameterProvider provider, int skip, out string where)
    {
        var names = new List<string>();
        var clauses = new StringBuilder();
        for (int i = skip; i < provider.GenericParameters.Count; i++)
        {
            GenericParameter parameter = provider.GenericParameters[i];
            names.Add(Id(parameter.Name));
            var parts = new List<string>();
            if (parameter.HasReferenceTypeConstraint) parts.Add("class");
            if (parameter.HasNotNullableValueTypeConstraint) parts.Add("struct");
            foreach (GenericParameterConstraint constraint in parameter.Constraints)
            {
                if (constraint.ConstraintType.FullName == "System.ValueType" || Unsupported(constraint.ConstraintType)) continue;
                parts.Add(Cs(constraint.ConstraintType));
            }
            if (parameter.HasDefaultConstructorConstraint && !parameter.HasNotNullableValueTypeConstraint) parts.Add("new()");
            if (parts.Count > 0) clauses.Append(" where ").Append(Id(parameter.Name)).Append(" : ").Append(string.Join(", ", parts));
        }
        where = clauses.ToString();
        return names.Count == 0 ? "" : "<" + string.Join(", ", names) + ">";
    }

    private string Parameters(IList<ParameterDefinition> parameters, bool extension, out List<string> outs, bool allowDefaults = true)
    {
        outs = new List<string>();
        var parts = new List<string>();
        for (int i = 0; i < parameters.Count; i++)
        {
            ParameterDefinition parameter = parameters[i];
            string name = Id(string.IsNullOrEmpty(parameter.Name) ? "arg" + i : parameter.Name);
            string modifier = "";
            if (parameter.ParameterType is ByReferenceType)
            {
                if (parameter.IsOut)
                {
                    modifier = "out ";
                    outs.Add(name);
                }
                else modifier = parameter.IsIn ? "in " : "ref ";
            }
            else if (parameter.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute")) modifier = "params ";
            string text = (i == 0 && extension ? "this " : "") + modifier + Cs(parameter.ParameterType) + " " + name;
            if (allowDefaults && modifier is "" or "in ")
            {
                if (parameter.HasConstant) text += " = " + Literal(parameter.Constant, parameter.ParameterType, Cs);
                else if (parameter.IsOptional) text += " = default";
            }
            parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    private static readonly Dictionary<string, string> Operators = new()
    {
        ["op_Addition"] = "+", ["op_Subtraction"] = "-", ["op_Multiply"] = "*", ["op_Division"] = "/", ["op_Modulus"] = "%",
        ["op_Equality"] = "==", ["op_Inequality"] = "!=", ["op_LessThan"] = "<", ["op_GreaterThan"] = ">", ["op_LessThanOrEqual"] = "<=",
        ["op_GreaterThanOrEqual"] = ">=", ["op_UnaryNegation"] = "-", ["op_UnaryPlus"] = "+", ["op_LogicalNot"] = "!", ["op_BitwiseAnd"] = "&",
        ["op_BitwiseOr"] = "|", ["op_ExclusiveOr"] = "^", ["op_OnesComplement"] = "~", ["op_LeftShift"] = "<<", ["op_RightShift"] = ">>",
        ["op_Increment"] = "++", ["op_Decrement"] = "--", ["op_True"] = "true", ["op_False"] = "false"
    };

    private static readonly Dictionary<string, string> Paired = new()
    {
        ["op_Equality"] = "op_Inequality", ["op_Inequality"] = "op_Equality", ["op_LessThan"] = "op_GreaterThan", ["op_GreaterThan"] = "op_LessThan",
        ["op_LessThanOrEqual"] = "op_GreaterThanOrEqual", ["op_GreaterThanOrEqual"] = "op_LessThanOrEqual", ["op_True"] = "op_False", ["op_False"] = "op_True"
    };

    // 原版在构造函数(静态的在类型初始化里)给字段、自动属性赋的常量初值：-1 的编号、默认为真的开关之类。
    // 只认"常量直接赋给本类型的字段"这一种写法；不是常量的不抄
    private readonly Dictionary<TypeDefinition, Dictionary<string, object>> _defaults = new();

    private Dictionary<string, object> Defaults(TypeDefinition type)
    {
        if (_defaults.TryGetValue(type, out Dictionary<string, object> found)) return found;
        found = new Dictionary<string, object>();
        _defaults[type] = found;
        foreach (MethodDefinition constructor in type.Methods.Where(m => m.IsConstructor && m.HasBody))
        {
            var body = constructor.Body.Instructions;
            for (int i = 0; i < body.Count; i++)
            {
                object value = Constant(body[i]);
                if (value == null) continue;
                int next = i + 1;
                if (next < body.Count && body[next].OpCode == OpCodes.Conv_I8)
                {
                    value = Convert.ToInt64(value);
                    next++;
                }
                if (next >= body.Count || body[next].Operand is not FieldReference field || field.DeclaringType.Resolve() != type) continue;
                bool isStatic = body[next].OpCode == OpCodes.Stsfld;
                if (!isStatic && (body[next].OpCode != OpCodes.Stfld || i == 0 || body[i - 1].OpCode != OpCodes.Ldarg_0)) continue;
                if (isStatic != constructor.IsStatic) continue;
                string name = field.Name;
                if (name.StartsWith("<") && name.EndsWith(">k__BackingField")) name = name.Substring(1, name.Length - 17);
                found.TryAdd(name, value);
            }
        }
        return found;
    }

    private static object Constant(Instruction instruction)
    {
        OpCode code = instruction.OpCode;
        if (code == OpCodes.Ldc_I4_M1) return -1;
        if (code == OpCodes.Ldc_I4_1) return 1;
        if (code == OpCodes.Ldc_I4_2) return 2;
        if (code == OpCodes.Ldc_I4_3) return 3;
        if (code == OpCodes.Ldc_I4_4) return 4;
        if (code == OpCodes.Ldc_I4_5) return 5;
        if (code == OpCodes.Ldc_I4_6) return 6;
        if (code == OpCodes.Ldc_I4_7) return 7;
        if (code == OpCodes.Ldc_I4_8) return 8;
        if (code == OpCodes.Ldc_I4_S) return (int)(sbyte)instruction.Operand;
        if (code == OpCodes.Ldc_I4) return (int)instruction.Operand;
        if (code == OpCodes.Ldc_I8) return (long)instruction.Operand;
        if (code == OpCodes.Ldc_R4) return (float)instruction.Operand;
        if (code == OpCodes.Ldc_R8) return (double)instruction.Operand;
        if (code == OpCodes.Ldstr) return (string)instruction.Operand;
        return null;
    }

    // 初值写成这个字段类型的字面量；写不了的(类型对不上)回空
    private string Initial(TypeDefinition owner, string member, TypeReference memberType)
    {
        if (!Defaults(owner).TryGetValue(member, out object value)) return null;
        string full = memberType.FullName;
        if (value is string text) return full == "System.String" ? Quote(text) : null;
        if (full == "System.String") return null;
        if (full == "System.Boolean") return Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0 ? "true" : null;
        if (full == "System.Single") return Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "f";
        if (full == "System.Double") return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "d";
        if (full == "System.Int64") return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L";
        if (full is "System.Int32" or "System.Int16" or "System.Byte" or "System.SByte" or "System.UInt16" or "System.UInt32" or "System.Char")
            return value is float or double ? null : "(" + Cs(memberType) + ")(" + Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + ")";
        TypeDefinition definition = null;
        try
        {
            definition = memberType.Resolve();
        }
        catch (Exception)
        {
            // 解析不了就不抄
        }
        if (definition != null && definition.IsEnum && value is not float and not double)
            return "(" + Cs(memberType) + ")(" + Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + ")";
        return null;
    }

    private const string NoInline = "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

    private string Miss(TypeDefinition owner, string member, TypeReference returns)
    {
        string label = Quote(owner.FullName.Replace('/', '.') + "::" + member);
        return returns == null || returns.FullName == "System.Void"
            ? "global::EmpireCraft.Server.Shim.Miss(" + label + ");"
            : "return global::EmpireCraft.Server.Shim.Miss<" + Cs(returns) + ">(" + label + ");";
    }

    // include.txt 里 new: 开头的类型：壳里凡是这种类型的字段一上来就建好(原版也是在声明处就建的容器)
    public static readonly HashSet<string> AutoNewTypes = new();

    private static bool AutoNew(TypeReference type) =>
        AutoNewTypes.Contains(Root(type).FullName) ||
        type is GenericInstanceType instance && instance.ElementType.Namespace == "System.Collections.Generic" &&
        Bare(instance.ElementType.Name) is "List" or "Dictionary" or "HashSet" or "Queue" or "Stack" or "SortedDictionary" or "LinkedList";

    private void WriteType(StringBuilder text, TypeDefinition type, string indent)
    {
        string fullName = type.FullName.Replace('/', '.');
        if (_handTypes.Contains(fullName)) return;
        HashSet<IMemberDefinition> members = _types[type];
        int outerGenerics = type.DeclaringType?.GenericParameters.Count ?? 0;
        string generics = Generics(type, outerGenerics, out string where);
        string name = Id(Bare(type.Name));

        if (type.IsEnum)
        {
            if (type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.FlagsAttribute")) text.Append(indent).AppendLine("[global::System.Flags]");
            FieldDefinition underlying = type.Fields.First(f => !f.IsStatic);
            text.Append(indent).Append("public enum ").Append(name).Append(" : ").Append(Cs(underlying.FieldType)).AppendLine().Append(indent).AppendLine("{");
            foreach (FieldDefinition field in type.Fields.Where(f => f.IsStatic && f.HasConstant))
                text.Append(indent).Append("    ").Append(Id(field.Name)).Append(" = ").Append(Convert.ToString(field.Constant, CultureInfo.InvariantCulture)).AppendLine(",");
            text.Append(indent).AppendLine("}");
            return;
        }
        if (IsDelegate(type))
        {
            MethodDefinition invoke = type.Methods.FirstOrDefault(m => m.Name == "Invoke");
            if (invoke == null || Unsupported(invoke.ReturnType) || invoke.Parameters.Any(p => Unsupported(p.ParameterType)))
            {
                Skip("delegate " + fullName);
                return;
            }
            text.Append(indent).Append("public delegate ").Append(Cs(invoke.ReturnType)).Append(' ').Append(name).Append(generics).Append('(')
                .Append(Parameters(invoke.Parameters, false, out _)).Append(')').Append(where).AppendLine(";");
            return;
        }

        bool isInterface = type.IsInterface, isStruct = type.IsValueType, isStatic = type.IsAbstract && type.IsSealed;
        var bases = new List<string>();
        if (!isInterface && !isStruct && type.BaseType != null && type.BaseType.FullName != "System.Object" && !Unsupported(type.BaseType))
            bases.Add(Cs(type.BaseType));
        foreach (InterfaceImplementation implemented in type.Interfaces)
        {
            TypeDefinition definition = null;
            try
            {
                definition = implemented.InterfaceType.Resolve();
            }
            catch (Exception)
            {
                // 留不下就不留
            }
            // 只留壳里有的外部接口(框架接口要整套实现，留不起)
            if (definition != null && _types.ContainsKey(definition) && !Unsupported(implemented.InterfaceType)) bases.Add(Cs(implemented.InterfaceType));
        }
        // 框架接口只留两样：可释放(using 语句要)、可遍历(foreach 与 LINQ 要)；实现见类体末尾
        bool disposable = false;
        string enumerates = null;
        if (!isStatic)
            foreach (InterfaceImplementation implemented in type.Interfaces)
            {
                TypeReference contract = implemented.InterfaceType;
                if (contract.FullName == "System.IDisposable") disposable = true;
                else if (contract is GenericInstanceType generic && generic.ElementType.FullName == "System.Collections.Generic.IEnumerable`1" &&
                         !Unsupported(generic.GenericArguments[0])) enumerates = Cs(generic.GenericArguments[0]);
            }
        if (disposable) bases.Add("global::System.IDisposable");
        if (enumerates != null) bases.Add("global::System.Collections.Generic.IEnumerable<" + enumerates + ">");
        CustomAttribute usage = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.AttributeUsageAttribute");
        if (usage != null && usage.ConstructorArguments.Count == 1)
        {
            var named = usage.Properties.Select(p => p.Name + " = " + ((bool)p.Argument.Value ? "true" : "false"));
            text.Append(indent).Append("[global::System.AttributeUsage((global::System.AttributeTargets)").Append(Convert.ToInt32(usage.ConstructorArguments[0].Value))
                .Append(string.Concat(named.Select(n => ", " + n))).AppendLine(")]");
        }
        text.Append(indent).Append("public ").Append(isStatic ? "static " : "").Append("partial ")
            .Append(isInterface ? "interface " : isStruct ? "struct " : "class ").Append(name).Append(generics);
        if (bases.Count > 0) text.Append(" : ").Append(string.Join(", ", bases));
        text.Append(where).AppendLine().Append(indent).AppendLine("{");
        string inner = indent + "    ";

        foreach (TypeDefinition nested in _types.Keys.Where(t => t.DeclaringType == type).OrderBy(t => t.Name, StringComparer.Ordinal))
            WriteType(text, nested, inner);

        // 成对的运算符补齐
        var all = new List<IMemberDefinition>(members);
        foreach (MethodDefinition method in members.OfType<MethodDefinition>().ToList())
            if (Paired.TryGetValue(method.Name, out string partnerName))
            {
                MethodDefinition partner = type.Methods.FirstOrDefault(m => m.Name == partnerName && m.Parameters.Count == method.Parameters.Count &&
                    m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName)));
                if (partner != null && !all.Contains(partner)) all.Add(partner);
            }

        bool hasDefaultConstructor = false;
        foreach (IMemberDefinition member in all.OrderBy(m => m is FieldDefinition ? 0 : m is PropertyDefinition ? 1 : m is EventDefinition ? 2 : 3)
                     .ThenBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.FullName, StringComparer.Ordinal))
        {
            if (Hand(type, member.Name == ".ctor" ? "ctor" : member.Name)) continue;
            switch (member)
            {
                case FieldDefinition field:
                {
                    if (Unsupported(field.FieldType))
                    {
                        Skip("field " + fullName + "::" + field.Name);
                        break;
                    }
                    text.Append(inner).Append("public ");
                    if (field.HasConstant && field.IsLiteral)
                        text.Append("const ").Append(Cs(field.FieldType)).Append(' ').Append(Id(field.Name)).Append(" = ").Append(Literal(field.Constant, field.FieldType, Cs));
                    else
                    {
                        text.Append(field.IsStatic ? "static " : "").Append(Cs(field.FieldType)).Append(' ').Append(Id(field.Name));
                        string initial = isStruct && !field.IsStatic ? null : Initial(type, field.Name, field.FieldType);
                        if (initial != null) text.Append(" = ").Append(initial);
                        else if (!isStruct && AutoNew(field.FieldType)) text.Append(" = new()");
                    }
                    text.AppendLine(";");
                    break;
                }
                case PropertyDefinition property:
                {
                    if (Unsupported(property.PropertyType) || property.Parameters.Any(p => Unsupported(p.ParameterType)) || property.Name.Contains('.'))
                    {
                        Skip("property " + fullName + "::" + property.Name);
                        break;
                    }
                    MethodDefinition accessor = property.GetMethod ?? property.SetMethod;
                    string modifier = isInterface ? "" : "public " + (accessor.IsStatic ? "static " : Virtual(accessor, isStruct));
                    bool indexer = property.Parameters.Count > 0;
                    string label = Quote(fullName + "::" + property.Name);
                    text.Append(inner).Append(modifier).Append(Cs(property.PropertyType)).Append(' ');
                    if (indexer)
                        text.Append("this[").Append(Parameters(property.Parameters, false, out _)).Append("] { get { return global::EmpireCraft.Server.Shim.Miss<")
                            .Append(Cs(property.PropertyType)).Append(">(").Append(label).AppendLine("); } set { } }");
                    else if (isInterface)
                        text.Append(Id(property.Name)).Append(" { get { return global::EmpireCraft.Server.Shim.Miss<").Append(Cs(property.PropertyType)).Append(">(")
                            .Append(label).AppendLine("); } set { } }");
                    else
                    {
                        string initial = isStruct && !accessor.IsStatic ? null : Initial(type, property.Name, property.PropertyType);
                        string tail = initial == null ? "" : " = " + initial + ";";
                        if (_patched.Contains(property)) text.Append(Id(property.Name)).Append(" { ").Append(NoInline).Append("get; ").Append(NoInline).Append("set; }").AppendLine(tail);
                        else text.Append(Id(property.Name)).Append(" { get; set; }").AppendLine(tail);
                    }
                    break;
                }
                case EventDefinition @event:
                {
                    if (Unsupported(@event.EventType))
                    {
                        Skip("event " + fullName + "::" + @event.Name);
                        break;
                    }
                    if (isInterface) break;
                    text.Append(inner).Append("public ").Append(@event.AddMethod.IsStatic ? "static " : "").Append("event ").Append(Cs(@event.EventType)).Append(' ')
                        .Append(Id(@event.Name)).AppendLine(";");
                    break;
                }
                case MethodDefinition method:
                {
                    if (method.Name.Contains('.') && !method.IsConstructor) break;
                    if (method.IsConstructor && method.IsStatic) break;
                    if (Unsupported(method.ReturnType) || method.ReturnType is ByReferenceType || method.Parameters.Any(p => Unsupported(p.ParameterType)))
                    {
                        Skip("method " + fullName + "::" + method.Name);
                        break;
                    }
                    bool extension = method.IsStatic && method.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");
                    string parameters = Parameters(method.Parameters, extension, out List<string> outs);
                    string assignOuts = string.Concat(outs.Select(o => o + " = default; "));
                    if (method.IsConstructor)
                    {
                        if (isStatic || isInterface) break;
                        if (method.Parameters.Count == 0)
                        {
                            if (isStruct) break;
                            hasDefaultConstructor = true;
                        }
                        text.Append(inner).Append("public ").Append(name).Append('(').Append(parameters).Append(") { ").Append(assignOuts).AppendLine("}");
                        break;
                    }
                    if (method.Name is "op_Implicit" or "op_Explicit")
                    {
                        text.Append(inner).Append("public static ").Append(method.Name == "op_Implicit" ? "implicit" : "explicit").Append(" operator ")
                            .Append(Cs(method.ReturnType)).Append('(').Append(parameters).Append(") { ").Append(Miss(type, method.Name, method.ReturnType)).AppendLine(" }");
                        break;
                    }
                    if (Operators.TryGetValue(method.Name, out string symbol))
                    {
                        text.Append(inner).Append("public static ").Append(Cs(method.ReturnType)).Append(" operator ").Append(symbol).Append('(').Append(parameters)
                            .Append(") { ").Append(Miss(type, method.Name, method.ReturnType)).AppendLine(" }");
                        break;
                    }
                    // 壳里的方法一律不许内联：模组的补丁随时可能挂上来
                    if (!isInterface) text.Append(inner).AppendLine(NoInline.TrimEnd());
                    string methodGenerics = Generics(method, 0, out string methodWhere);
                    // 覆盖方法不许重写约束
                    bool overrides = method.IsVirtual && !method.IsNewSlot && !isStruct && BaseMethod(method) != null;
                    text.Append(inner).Append(isInterface ? "" : "public " + (method.IsStatic ? "static " : Virtual(method, isStruct))).Append(Cs(method.ReturnType)).Append(' ')
                        .Append(Id(method.Name)).Append(methodGenerics).Append('(').Append(parameters).Append(')').Append(overrides ? "" : methodWhere);
                    if (overrides && !isInterface)
                    {
                        var passed = new List<string>();
                        for (int i = 0; i < method.Parameters.Count; i++)
                        {
                            ParameterDefinition parameter = method.Parameters[i];
                            string argument = Id(string.IsNullOrEmpty(parameter.Name) ? "arg" + i : parameter.Name);
                            if (parameter.ParameterType is ByReferenceType) argument = (parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ") + argument;
                            passed.Add(argument);
                        }
                        bool returnsVoid = method.ReturnType.FullName == "System.Void";
                        text.Append(" { global::EmpireCraft.Server.Shim.Miss(").Append(Quote(fullName + "::" + method.Name)).Append("); ")
                            .Append(returnsVoid ? "" : "return ").Append("base.").Append(Id(method.Name)).Append(methodGenerics).Append('(').Append(string.Join(", ", passed)).AppendLine("); }");
                    }
                    // 遍历的入口不能回空(foreach 一上来就空引用)：没有手写的真东西时给个空的
                    else if (!isInterface && !method.IsStatic && method.Name == "GetEnumerator" && method.Parameters.Count == 0 &&
                             method.ReturnType is GenericInstanceType walker && walker.ElementType.FullName == "System.Collections.Generic.IEnumerator`1")
                        text.Append(" { return global::EmpireCraft.Server.Shim.Items<").Append(Cs(walker.GenericArguments[0])).Append(">(this, ")
                            .Append(Quote(fullName + "::GetEnumerator")).AppendLine("); }");
                    else text.Append(" { ").Append(assignOuts).Append(Miss(type, method.Name, method.ReturnType)).AppendLine(" }");
                    break;
                }
            }
        }
        if (disposable && !isInterface && !all.OfType<MethodDefinition>().Any(m => m.Name == "Dispose" && m.Parameters.Count == 0))
            text.Append(inner).AppendLine("public void Dispose() { }");
        if (enumerates != null && !isInterface)
        {
            // 真正的遍历由手写的 partial 实现 IShimItems<T> 给；没给就是空的，并记一笔
            string label = Quote(fullName + "::GetEnumerator");
            text.Append(inner).Append("global::System.Collections.Generic.IEnumerator<").Append(enumerates).Append("> global::System.Collections.Generic.IEnumerable<")
                .Append(enumerates).Append(">.GetEnumerator() { return global::EmpireCraft.Server.Shim.Items<").Append(enumerates).Append(">(this, ").Append(label).AppendLine("); }");
            text.Append(inner).Append("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() { return global::EmpireCraft.Server.Shim.Items<")
                .Append(enumerates).Append(">(this, ").Append(label).AppendLine("); }");
        }
        if (!isInterface && !isStruct && !isStatic && !hasDefaultConstructor && !Hand(type, "ctor"))
            text.Append(inner).Append("public ").Append(name).AppendLine("() { }");
        text.Append(indent).AppendLine("}");
    }

    // 虚方法：头一回声明的写 virtual(抽象的也给个空体)，覆盖基类的写 override；接口实现那种(final+newslot)什么都不写
    private static string Virtual(MethodDefinition method, bool isStruct)
    {
        if (isStruct || !method.IsVirtual) return "";
        if (!method.IsNewSlot) return BaseMethod(method) != null ? "override " : "virtual ";
        return method.IsFinal ? "" : "virtual ";
    }

    public void Write(string outDir)
    {
        var report = new StringBuilder();
        foreach (var byAssembly in _types.Keys.Where(t => t.DeclaringType == null).GroupBy(t => t.Module.Assembly.Name.Name).OrderBy(g => g.Key))
        {
            var text = new StringBuilder();
            text.AppendLine("// <auto-generated/> 由 tools/ShimGen 生成，别手改：手写的实现放 Shim/ 下，接手的成员登记进 handwritten.txt。");
            text.AppendLine("#pragma warning disable");
            foreach (var byNamespace in byAssembly.GroupBy(t => t.Namespace ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string indent = "";
                if (byNamespace.Key.Length > 0)
                {
                    text.Append("namespace ").AppendLine(byNamespace.Key).AppendLine("{");
                    indent = "    ";
                }
                foreach (TypeDefinition type in byNamespace.OrderBy(t => t.Name, StringComparer.Ordinal)) WriteType(text, type, indent);
                if (byNamespace.Key.Length > 0) text.AppendLine("}");
            }
            File.WriteAllText(Path.Combine(outDir, (byAssembly.Key == _self ? "EmpireCraft.Excluded" : byAssembly.Key) + ".g.cs"), text.ToString(), new UTF8Encoding(false));
        }
        // 报告：每个类型用到了哪些成员、各被引用多少回(手写实现时按这个排先后)
        foreach (var pair in _types.OrderByDescending(p => p.Value.Sum(m => _refs.GetValueOrDefault(m))))
        {
            string fullName = pair.Key.FullName.Replace('/', '.');
            report.Append(_handTypes.Contains(fullName) ? "[hand] " : "").Append(pair.Key.Module.Assembly.Name.Name).Append("  ").Append(fullName)
                .Append("  refs=").Append(pair.Value.Sum(m => _refs.GetValueOrDefault(m))).AppendLine();
            foreach (IMemberDefinition member in pair.Value.OrderByDescending(m => _refs.GetValueOrDefault(m)))
                report.Append("    ").Append(_refs.GetValueOrDefault(member).ToString().PadLeft(6)).Append("  ").Append(Signature(member)).AppendLine();
        }
        File.WriteAllText(Path.Combine(outDir, "shim-report.txt"), report.ToString(), new UTF8Encoding(false));
        // 补丁目标清单：手写接手这些成员时要自己标上不许内联(服务器启动时会核对)
        File.WriteAllLines(Path.Combine(outDir, "patch-targets.txt"),
            _patched.Select(m => m.DeclaringType.FullName.Replace('/', '.') + "::" + m.Name).Distinct().OrderBy(line => line, StringComparer.Ordinal), new UTF8Encoding(false));
    }

    private static string Signature(IMemberDefinition member) => member switch
    {
        FieldDefinition field => (field.IsStatic ? "static " : "") + field.FieldType.Name + " " + field.Name,
        PropertyDefinition property => ((property.GetMethod ?? property.SetMethod).IsStatic ? "static " : "") + property.PropertyType.Name + " " + property.Name + " { }",
        MethodDefinition method => (method.IsStatic ? "static " : "") + method.ReturnType.Name + " " + method.Name + "(" +
                                   string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name + " " + p.Name)) + ")",
        _ => member.Name
    };
}
