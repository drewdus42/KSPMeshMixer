using System.Collections.Generic;
using Redux.Ksp1Import.Config;

namespace KSPMeshMixer
{
    /// <summary>Adapts the importer's config node to <see cref="IB9Node"/> so parsing stays game-free.</summary>
    internal sealed class Ksp1NodeAdapter : IB9Node
    {
        private readonly Ksp1ConfigNode _node;

        public Ksp1NodeAdapter(Ksp1ConfigNode node)
        {
            _node = node;
        }

        public string Name
        {
            get { return _node == null ? "" : _node.Name; }
        }

        public IEnumerable<KeyValuePair<string, string>> Values
        {
            get
            {
                if (_node == null || _node.Values == null)
                {
                    yield break;
                }

                foreach (Ksp1ConfigNode.Value value in _node.Values)
                {
                    yield return new KeyValuePair<string, string>(value.Name, value.RawValue);
                }
            }
        }

        public IEnumerable<IB9Node> Nodes
        {
            get
            {
                if (_node == null || _node.Nodes == null)
                {
                    yield break;
                }

                foreach (Ksp1ConfigNode child in _node.Nodes)
                {
                    yield return new Ksp1NodeAdapter(child);
                }
            }
        }

        public string GetValue(string name, string fallback = null)
        {
            return _node == null ? fallback : _node.GetValue(name, fallback);
        }

        public IEnumerable<string> GetValues(string name)
        {
            return _node == null ? new string[0] : _node.GetValues(name);
        }

        public IEnumerable<IB9Node> GetNodes(string name)
        {
            if (_node == null)
            {
                yield break;
            }

            foreach (Ksp1ConfigNode child in _node.GetNodes(name))
            {
                yield return new Ksp1NodeAdapter(child);
            }
        }

        public static IB9Node Wrap(Ksp1ConfigNode node)
        {
            return node == null ? null : new Ksp1NodeAdapter(node);
        }
    }
}
