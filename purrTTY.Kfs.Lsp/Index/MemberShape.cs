// RULE FOR THIS FILE: no game type.

using System.Globalization;
using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>One member of a <see cref="MemberShape"/>.</summary>
/// <param name="Name">The member name (<c>thruster</c>, <c>altitude_m</c>).</param>
/// <param name="Entry">Its hover / completion entry (kind <see cref="EntryKind.Field"/>).</param>
/// <param name="Shape">The member's own members when it is a struct (or an array of structs), else null.</param>
public sealed record ShapeMember(string Name, IndexEntry Entry, MemberShape? Shape);

/// <summary>
/// The members of a struct type the server knows without a parser: the command and sensor blocks,
/// <c>kfs_sys_t</c>, the message structs and their element types, the vector types. Immutable.
/// </summary>
public sealed class MemberShape
{
    private readonly Dictionary<string, ShapeMember> _byName;

    /// <summary>A shape named <paramref name="typeName"/> with <paramref name="members"/> (in declaration order).</summary>
    public MemberShape(string typeName, IReadOnlyList<ShapeMember> members)
    {
        TypeName = typeName;
        Members = members;
        _byName = new Dictionary<string, ShapeMember>(StringComparer.Ordinal);
        foreach (ShapeMember m in members) _byName.TryAdd(m.Name, m);
    }

    /// <summary>The C type name (<c>kfs_msg_flight_data_t</c>), or a description for an anonymous struct.</summary>
    public string TypeName { get; }

    /// <summary>The members, in declaration order.</summary>
    public IReadOnlyList<ShapeMember> Members { get; }

    /// <summary>The member named <paramref name="name"/>, or null.</summary>
    public ShapeMember? Find(string name) => _byName.GetValueOrDefault(name);
}

/// <summary>
/// Builds <see cref="MemberShape"/>s from flat leaf paths (<c>thruster[3].pulse_s</c>,
/// <c>engine[0].core_kind[1]</c>, <c>I_body_kgm2.m[0][2]</c>) as the layout and the message
/// catalogue list them: every <c>[n]</c> is an array dimension, its length the largest index + 1.
/// </summary>
public static partial class ShapeBuilder
{
    /// <summary>A leaf of a block: its full path, its table type (<c>float32</c>, <c>uint32</c>, …) and byte offset.</summary>
    public readonly record struct Leaf(string Path, string Type, int Offset, bool OneShot = false);

    /// <summary>A node of the path tree while it is built.</summary>
    public sealed class Node
    {
        internal Node(string name, string path) { Name = name; Path = path; }

        /// <summary>The member name.</summary>
        public string Name { get; }

        /// <summary>The path from the root without indices (<c>thruster.pulse_s</c>).</summary>
        public string Path { get; }

        /// <summary>Array lengths, outermost first; empty for no array.</summary>
        public List<int> Dimensions { get; } = [];

        /// <summary>The table type of a leaf, else null.</summary>
        public string? LeafType { get; internal set; }

        /// <summary>The lowest offset of any leaf below (in the first element of every array).</summary>
        public int Offset { get; internal set; } = int.MaxValue;

        /// <summary>True when the leaf is a one-shot command field.</summary>
        public bool OneShot { get; internal set; }

        /// <summary>Children in first-seen order.</summary>
        public List<Node> Children { get; } = [];

        internal Dictionary<string, Node> ByName { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The root node of the tree of <paramref name="leaves"/>.</summary>
    public static Node Tree(IEnumerable<Leaf> leaves)
    {
        var root = new Node("", "");
        foreach (Leaf leaf in leaves)
        {
            Node node = root;
            string[] segments = leaf.Path.Split('.');
            bool firstElement = true;
            for (int s = 0; s < segments.Length; s++)
            {
                Match m = Segment().Match(segments[s]);
                string name = m.Success ? m.Groups["name"].Value : segments[s];
                if (!node.ByName.TryGetValue(name, out Node? child))
                {
                    string path = node.Path.Length == 0 ? name : node.Path + "." + name;
                    child = new Node(name, path);
                    node.ByName[name] = child;
                    node.Children.Add(child);
                }
                if (m.Success)
                {
                    int d = 0;
                    foreach (Capture c in m.Groups["index"].Captures)
                    {
                        int index = int.Parse(c.Value, CultureInfo.InvariantCulture);
                        if (index != 0) firstElement = false;
                        while (child.Dimensions.Count <= d) child.Dimensions.Add(0);
                        child.Dimensions[d] = Math.Max(child.Dimensions[d], index + 1);
                        d++;
                    }
                }
                if (firstElement) child.Offset = Math.Min(child.Offset, leaf.Offset);
                if (s == segments.Length - 1)
                {
                    child.LeafType = leaf.Type;
                    child.OneShot |= leaf.OneShot;
                }
                node = child;
            }
        }
        return root;
    }

    /// <summary>
    /// Converts a tree into a shape named <paramref name="typeName"/>; <paramref name="describe"/> gives
    /// each member's C type and documentation, <paramref name="childTypeName"/> the type name of a
    /// struct member's own shape.
    /// </summary>
    public static MemberShape ToShape(Node root, string typeName, Func<Node, (string CType, string Documentation)> describe,
        Func<Node, string>? childTypeName = null)
    {
        var members = new List<ShapeMember>();
        foreach (Node child in root.Children)
        {
            (string ctype, string doc) = describe(child);
            MemberShape? shape = child.Children.Count > 0
                ? ToShape(child, childTypeName?.Invoke(child) ?? "struct", describe, childTypeName)
                : null;
            string dims = string.Concat(child.Dimensions.Select(d => $"[{d}]"));
            string declaration = ctype == "struct" ? $"struct {{ … }} {child.Name}{dims}" : $"{ctype} {child.Name}{dims}";
            members.Add(new ShapeMember(child.Name, new IndexEntry(child.Name, EntryKind.Field, declaration, declaration + ";", doc)
            {
                SortGroup = 1,
            }, shape));
        }
        return new MemberShape(typeName, members);
    }

    /// <summary>The C type of a table type (<c>float32</c> → <c>float</c>, <c>bool</c> → <c>kfs_bool32_t</c>).</summary>
    public static string CType(string tableType) => tableType switch
    {
        "float32" or "float" => "float",
        "float64" or "double" => "double",
        "uint32" => "uint32_t",
        "int32" => "int32_t",
        "uint16" => "uint16_t",
        "int16" => "int16_t",
        "uint8" => "uint8_t",
        "int64" => "int64_t",
        "uint64" => "uint64_t",
        "bool" => "kfs_bool32_t",
        _ => tableType,
    };

    /// <summary>A fixed shape of scalar members (<c>vec3</c>: x, y, z).</summary>
    public static MemberShape Vector(string typeName, string ctype, params string[] names) =>
        new(typeName, names.Select((n, i) => new ShapeMember(n, new IndexEntry(n, EntryKind.Field, $"{ctype} {n}", $"{ctype} {n};",
            $"Component {Markup.Code(n)} of {Markup.Code(typeName)}, at byte offset {i * (ctype == "double" ? 8 : 4)}.") { SortGroup = 1 }, null)).ToArray());

    /// <summary>A 3×3 matrix shape: one member <c>m[3][3]</c>.</summary>
    public static MemberShape Matrix(string typeName, string ctype) =>
        new(typeName, [new ShapeMember("m", new IndexEntry("m", EntryKind.Field, $"{ctype} m[3][3]", $"{ctype} m[3][3];",
            $"The elements of {Markup.Code(typeName)}, row-major: {Markup.Code("m[row][column]")}.") { SortGroup = 1 }, null)]);

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)(?:\[(?<index>\d+)\])+$")]
    private static partial Regex Segment();
}
