using System.Text;
using System.Xml;
using MovingHeadSequencer.Choreography;
using MovingHeadSequencer.Sequences;

namespace MovingHeadSequencer.Xsq;

internal sealed class XsqDocumentEditor
{
    public XmlDocument Document { get; }

    private XsqDocumentEditor(XmlDocument document) => Document = document;

    public static XsqDocumentEditor Load(string path)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(path);
        return new XsqDocumentEditor(document);
    }

    public IReadOnlyDictionary<string, int> AddEffectDefinitions(EffectDefinitions definitions)
    {
        var effectDb = Document.SelectSingleNode("/xsequence/EffectDB")
            ?? throw new InvalidDataException("EffectDB was not found.");
        var references = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var definition in definitions.Items)
        {
            references[definition.Key] = effectDb.SelectNodes("Effect")?.Count ?? 0;
            var effect = Document.CreateElement("Effect");
            effect.InnerText = definition.Value;
            if (effectDb.LastChild?.NodeType == XmlNodeType.Whitespace)
            {
                effectDb.InsertBefore(Document.CreateWhitespace("\r\n    "), effectDb.LastChild);
                effectDb.InsertBefore(effect, effectDb.LastChild);
            }
            else
            {
                effectDb.AppendChild(effect);
            }
        }

        return references;
    }

    public void AddModelScaffolding(IEnumerable<string> modelNames)
    {
        var displayElements = Document.SelectSingleNode("/xsequence/DisplayElements")
            ?? throw new InvalidDataException("DisplayElements was not found.");
        var elementEffects = Document.SelectSingleNode("/xsequence/ElementEffects")
            ?? throw new InvalidDataException("ElementEffects was not found.");

        foreach (var modelName in modelNames)
        {
            if (FindModel("DisplayElements", modelName) is null)
            {
                var displayElement = Document.CreateElement("Element");
                displayElement.SetAttribute("collapsed", "0");
                displayElement.SetAttribute("type", "model");
                displayElement.SetAttribute("name", modelName);
                displayElement.SetAttribute("visible", "1");
                AddChildBeforeEnd(displayElements, displayElement, "    ");
            }

            if (FindModel("ElementEffects", modelName) is null)
            {
                var effectElement = Document.CreateElement("Element");
                effectElement.SetAttribute("type", "model");
                effectElement.SetAttribute("name", modelName);
                AddFormattedChild(effectElement, Document.CreateElement("EffectLayer"), "      ");
                effectElement.AppendChild(Document.CreateWhitespace("\r\n    "));
                AddChildBeforeEnd(elementEffects, effectElement, "    ");
            }
        }
    }

    public void RemoveModels(IEnumerable<string> modelNames)
    {
        foreach (var modelName in modelNames)
        {
            foreach (var sectionName in new[] { "DisplayElements", "ElementEffects" })
            {
                foreach (var node in FindModels(sectionName, modelName))
                {
                    node.ParentNode?.RemoveChild(node);
                }
            }
        }
    }

    public void ExpandLegacyFixtureModels(int headCount)
    {
        if (headCount == 4)
        {
            return;
        }

        var sourceEffects = Enumerable.Range(1, 4)
            .Select(index => FindModel("ElementEffects", $"Mover{index}")
                ?? throw new InvalidDataException($"Source fixture effect element 'Mover{index}' was not found."))
            .Select(element => (XmlElement)element.CloneNode(deep: true))
            .ToArray();
        AddModelScaffolding(Enumerable.Range(1, headCount).Select(index => $"Mover{index}"));

        for (var nodeIndex = 0; nodeIndex < headCount; nodeIndex++)
        {
            var destinationName = $"Mover{nodeIndex + 1}";
            var sourceIndex = (int)Math.Round(
                3d * nodeIndex / (headCount - 1),
                MidpointRounding.AwayFromZero);
            var existing = FindModel("ElementEffects", destinationName)
                ?? throw new InvalidDataException($"Destination fixture '{destinationName}' was not found.");
            var replacement = (XmlElement)sourceEffects[sourceIndex].CloneNode(deep: true);
            replacement.SetAttribute("name", destinationName);
            existing.ParentNode!.ReplaceChild(replacement, existing);
        }
        if (headCount < 4)
        {
            RemoveModels(Enumerable.Range(headCount + 1, 4 - headCount)
                .Select(index => $"Mover{index}"));
        }
    }

    public void ResetControlTrack(string modelName)
    {
        var element = FindModel("ElementEffects", modelName)
            ?? throw new InvalidDataException($"Control element '{modelName}' was not found.");
        foreach (var strand in SelectElements(element, "Strand").ToArray())
        {
            element.RemoveChild(strand);
        }
        foreach (var effect in SelectElements(element, "EffectLayer/Effect").ToArray())
        {
            effect.ParentNode?.RemoveChild(effect);
        }
    }

    public void SetControlTrack(
        string modelName,
        IReadOnlyDictionary<int, IReadOnlyList<TrackEvent>> tracks,
        IReadOnlyDictionary<string, int> references,
        int palette,
        int headCount,
        string effectName = "DMX")
    {
        var element = FindModel("ElementEffects", modelName)
            ?? throw new InvalidDataException($"Model effect element '{modelName}' was not found.");
        if (element.SelectSingleNode("Strand") is not null || element.SelectSingleNode("EffectLayer/Effect") is not null)
        {
            throw new InvalidDataException($"Model '{modelName}' already contains effects; refusing to overwrite it.");
        }

        var strand = Document.CreateElement("Strand");
        strand.SetAttribute("index", "0");
        if (tracks.Count != headCount)
        {
            throw new InvalidDataException($"Model '{modelName}' received {tracks.Count} tracks; expected {headCount}.");
        }
        foreach (var (nodeIndex, events) in tracks.OrderBy(pair => pair.Key))
        {
            var node = Document.CreateElement("Node");
            node.SetAttribute("index", nodeIndex.ToString());
            for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                var cueEvent = events[eventIndex];
                if (!references.TryGetValue(cueEvent.Key, out var reference))
                {
                    throw new InvalidDataException(
                        $"Unknown movement primitive '{cueEvent.Key}' on '{modelName}'.");
                }

                var effect = Document.CreateElement("Effect");
                effect.SetAttribute("ref", reference.ToString());
                effect.SetAttribute("name", effectName);
                if (eventIndex > 0)
                {
                    effect.SetAttribute("id", eventIndex.ToString());
                }
                effect.SetAttribute("startTime", cueEvent.Start.ToString());
                effect.SetAttribute("endTime", cueEvent.End.ToString());
                effect.SetAttribute("palette", palette.ToString());
                AddFormattedChild(node, effect, "          ");
            }

            node.AppendChild(Document.CreateWhitespace("\r\n        "));
            AddFormattedChild(strand, node, "        ");
        }

        strand.AppendChild(Document.CreateWhitespace("\r\n      "));
        if (element.LastChild?.NodeType == XmlNodeType.Whitespace)
        {
            element.InsertBefore(Document.CreateWhitespace("\r\n      "), element.LastChild);
            element.InsertBefore(strand, element.LastChild);
        }
        else
        {
            element.AppendChild(strand);
        }
    }

    public void SetRootEffects(
        string modelName,
        IReadOnlyList<RootEffectEvent> events,
        IReadOnlyDictionary<string, int> references,
        int palette,
        string effectName)
    {
        var element = FindModel("ElementEffects", modelName)
            ?? throw new InvalidDataException($"Model effect element '{modelName}' was not found.");
        var effectLayer = element.SelectSingleNode("EffectLayer") as XmlElement;
        if (effectLayer is null || effectLayer.SelectNodes("Effect")?.Count > 0)
        {
            throw new InvalidDataException(
                $"Root effect layer for '{modelName}' is missing or already populated.");
        }

        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            var cueEvent = events[eventIndex];
            var effect = Document.CreateElement("Effect");
            effect.SetAttribute("ref", references[cueEvent.Key].ToString());
            effect.SetAttribute("name", effectName);
            if (eventIndex > 0)
            {
                effect.SetAttribute("id", eventIndex.ToString());
            }
            effect.SetAttribute("startTime", cueEvent.Start.ToString());
            effect.SetAttribute("endTime", cueEvent.End.ToString());
            effect.SetAttribute("palette", palette.ToString());
            AddFormattedChild(effectLayer, effect, "        ");
        }
        effectLayer.AppendChild(Document.CreateWhitespace("\r\n      "));
    }

    public void AssertControlTrack(
        string modelName,
        int expectedEnd,
        int headCount,
        bool allowGaps = false,
        int frameIntervalMs = 25)
    {
        var effectDbCount = Document.SelectNodes("/xsequence/EffectDB/Effect")?.Count ?? 0;
        var paletteCount = Document.SelectNodes("/xsequence/ColorPalettes/ColorPalette")?.Count ?? 0;
        var element = FindModel("ElementEffects", modelName)
            ?? throw new InvalidDataException($"Model effect element '{modelName}' was not found.");
        var nodes = SelectElements(element, "Strand[@index='0']/Node").ToArray();
        if (nodes.Length != headCount)
        {
            throw new InvalidDataException(
                $"Expected {headCount} nodes on '{modelName}'; found {nodes.Length}.");
        }

        foreach (var node in nodes)
        {
            var effects = SelectElements(node, "Effect").ToArray();
            if (effects.Length == 0)
            {
                throw new InvalidDataException($"Node {node.GetAttribute("index")} on '{modelName}' has no effects.");
            }

            var previousEnd = 0;
            foreach (var effect in effects)
            {
                var start = int.Parse(effect.GetAttribute("startTime"));
                var end = int.Parse(effect.GetAttribute("endTime"));
                var reference = int.Parse(effect.GetAttribute("ref"));
                var palette = int.Parse(effect.GetAttribute("palette"));
                if ((allowGaps && start < previousEnd) || (!allowGaps && start != previousEnd))
                {
                    throw new InvalidDataException(
                        $"Gap or overlap on '{modelName}' node {node.GetAttribute("index")} at {start} ms; " +
                        $"previous effect ends at {previousEnd} ms.");
                }
                if (frameIntervalMs > 1 && (start % frameIntervalMs != 0 || end % frameIntervalMs != 0))
                {
                    throw new InvalidDataException(
                        $"Off-grid effect on '{modelName}' node {node.GetAttribute("index")}: {start}-{end}.");
                }
                if (end <= start || reference < 0 || reference >= effectDbCount)
                {
                    throw new InvalidDataException(
                        $"Invalid effect on '{modelName}' node {node.GetAttribute("index")}: " +
                        $"ref={reference}, {start}-{end}.");
                }
                if (palette < 0 || palette >= paletteCount)
                {
                    throw new InvalidDataException(
                        $"Invalid palette {palette} on '{modelName}' node {node.GetAttribute("index")}.");
                }
                previousEnd = end;
            }

            if (previousEnd != expectedEnd)
            {
                throw new InvalidDataException(
                    $"'{modelName}' node {node.GetAttribute("index")} ends at {previousEnd} ms; " +
                    $"expected {expectedEnd} ms.");
            }
        }
    }

    public int GetControlNodeCount(string modelName) =>
        FindModel("ElementEffects", modelName)?.SelectNodes("Strand[@index='0']/Node")?.Count ?? 0;

    public void AssertRootEffects(
        string modelName,
        IReadOnlyList<RootEffectEvent> expectedEvents,
        int frameIntervalMs = 25)
    {
        var effectDbCount = Document.SelectNodes("/xsequence/EffectDB/Effect")?.Count ?? 0;
        var paletteCount = Document.SelectNodes("/xsequence/ColorPalettes/ColorPalette")?.Count ?? 0;
        var element = FindModel("ElementEffects", modelName)
            ?? throw new InvalidDataException($"Model effect element '{modelName}' was not found.");
        var effects = SelectElements(element, "EffectLayer/Effect").ToArray();
        if (effects.Length != expectedEvents.Count)
        {
            throw new InvalidDataException(
                $"Expected {expectedEvents.Count} root effects on '{modelName}'; found {effects.Length}.");
        }

        for (var index = 0; index < effects.Length; index++)
        {
            var effect = effects[index];
            var expected = expectedEvents[index];
            var start = int.Parse(effect.GetAttribute("startTime"));
            var end = int.Parse(effect.GetAttribute("endTime"));
            var reference = int.Parse(effect.GetAttribute("ref"));
            var palette = int.Parse(effect.GetAttribute("palette"));
            if (start != expected.Start || end != expected.End)
            {
                throw new InvalidDataException(
                    $"Root effect {index} on '{modelName}' is {start}-{end}; " +
                    $"expected {expected.Start}-{expected.End}.");
            }
            if (frameIntervalMs > 1 && (start % frameIntervalMs != 0 || end % frameIntervalMs != 0))
            {
                throw new InvalidDataException(
                    $"Off-grid root effect on '{modelName}': {start}-{end}.");
            }
            if (end <= start || reference < 0 || reference >= effectDbCount)
            {
                throw new InvalidDataException(
                    $"Invalid root effect on '{modelName}': ref={reference}, {start}-{end}.");
            }
            if (palette < 0 || palette >= paletteCount)
            {
                throw new InvalidDataException(
                    $"Invalid root effect palette {palette} on '{modelName}'.");
            }
        }
    }

    public void Save(string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };
        using var writer = XmlWriter.Create(path, settings);
        Document.Save(writer);
    }

    private XmlElement? FindModel(string sectionName, string modelName)
    {
        var matches = FindModels(sectionName, modelName).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidDataException(
                $"Sequence contains multiple '{modelName}' elements in {sectionName}."),
        };
    }

    private IEnumerable<XmlElement> FindModels(string sectionName, string modelName)
    {
        var section = Document.SelectSingleNode($"/xsequence/{sectionName}")
            ?? throw new InvalidDataException($"Sequence section '{sectionName}' was not found.");
        return SelectElements(section, "Element[@type='model']")
            .Where(element => element.GetAttribute("name").Equals(modelName, StringComparison.Ordinal));
    }

    private void AddFormattedChild(XmlNode parent, XmlNode child, string indent)
    {
        parent.AppendChild(Document.CreateWhitespace($"\r\n{indent}"));
        parent.AppendChild(child);
    }

    private void AddChildBeforeEnd(XmlNode parent, XmlNode child, string indent)
    {
        if (parent.LastChild?.NodeType == XmlNodeType.Whitespace)
        {
            parent.InsertBefore(Document.CreateWhitespace($"\r\n{indent}"), parent.LastChild);
            parent.InsertBefore(child, parent.LastChild);
        }
        else
        {
            AddFormattedChild(parent, child, indent);
        }
    }

    private static IEnumerable<XmlElement> SelectElements(XmlNode node, string xpath) =>
        node.SelectNodes(xpath)?.Cast<XmlElement>() ?? [];
}