using System.Collections.Generic;

namespace KSPMeshMixer
{
    /// <summary>
    /// The shape of a KSP config node, minus the game types. The mod adapts a
    /// <c>Ksp1ConfigNode</c> to this and the tests use their own implementation, so the B9 parsing
    /// below runs (and is verified) without the game.
    /// </summary>
    internal interface IB9Node
    {
        string Name { get; }

        /// <summary>Every key/value on the node, in file order (needed for shader-property keys).</summary>
        IEnumerable<KeyValuePair<string, string>> Values { get; }

        IEnumerable<IB9Node> Nodes { get; }

        string GetValue(string name, string fallback = null);

        IEnumerable<string> GetValues(string name);

        IEnumerable<IB9Node> GetNodes(string name);
    }
}
