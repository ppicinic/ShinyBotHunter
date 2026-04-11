using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ShinyBotHunter.Macro
{
    public class MacroRegistry
    {
        public static MacroRegistry Instance { get; private set; } = new MacroRegistry();
        private Dictionary<GameFlavor, Dictionary<string, CompiledMacroDefinition>> macros;

        private MacroRegistry()
        {
            macros = new();
            LoadAllMacros();
        }

        private void LoadAllMacros()
        {
            IEnumerable<string> files = Directory.EnumerateFiles("./Macro/Configs/", "*.*", SearchOption.AllDirectories);
            
            List<RawMacroDefinition> rawDefs = DeserializeAllFiles(files);
            List<IntermediateMacroDefinition> intermediateMacroDefs = PreCompile(rawDefs);
            List<GameFlavor> allFlavors = GetAllFlavors(intermediateMacroDefs);
            foreach (GameFlavor flavor in allFlavors)
            {
                var allFlavorDefs = intermediateMacroDefs.Where(x => x.MacroFlavors.Contains(flavor)).ToDictionary(x=> x.Name);
                Dictionary<string, CompiledMacroDefinition> macroDefs = CompileMacros(allFlavorDefs);
                macros[flavor] = macroDefs;
            }
        }

        private Dictionary<string, CompiledMacroDefinition> CompileMacros(Dictionary<string, IntermediateMacroDefinition> defs)
        {
            Dictionary<string, CompiledMacroDefinition> macros = new Dictionary<string, CompiledMacroDefinition>();
            List<string> sortedKeys = TopologicalSort(defs);
            foreach(string key in sortedKeys)
            {
                var compiledMacro = CompileMacro(defs[key], macros);
                macros[compiledMacro.Name] = compiledMacro;
            }
            return macros;
        }

        private CompiledMacroDefinition CompileMacro(IntermediateMacroDefinition def, Dictionary<string, CompiledMacroDefinition> macros)
        {
            List<string> frames = new List<string>();
            foreach(var step in def.Steps)
            {
                if (step.IsMacro)
                {
                    frames.AddRange(macros[step.MacroName].Frames);
                }
                else
                {
                    frames.Add(String.Join(":", step.Action));
                }
            }
            return new CompiledMacroDefinition()
            {
                Name = def.Name,
                Frames = frames,
                Macro = String.Join(";", frames)
            };
        }

        private List<string> TopologicalSort(Dictionary<string, IntermediateMacroDefinition> names)
        {
            var deps = new Dictionary<string, List<string>>();
            var inDegree = new Dictionary<string, int>();

            foreach (var name in names.Keys)
            {
                deps[name] = names[name].Dependencies;
                inDegree[name] = 0;
            }

            foreach (var (node, dependencies) in deps)
            {
                foreach (var dep in dependencies)
                {
                    if (!inDegree.ContainsKey(dep))
                        throw new Exception($"Macro '{node}' depends on '{dep}' which does not exist.");
                    inDegree[node]++;
                }
            }

            var queue = new Queue<string>(inDegree.Where(k => k.Value == 0).Select(k => k.Key));
            var sorted = new List<string>();

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sorted.Add(node);

                foreach (var (dependent, dependencies) in deps)
                {
                    if (!dependencies.Contains(node)) continue;
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }

            if (sorted.Count != names.Count)
                throw new Exception("Circular dependency detected in macros.");

            return sorted;
        }

        private List<GameFlavor> GetAllFlavors(List<IntermediateMacroDefinition> defs)
        {
            HashSet<GameFlavor> flavors = new HashSet<GameFlavor>();
            foreach (var def in defs) {
                def.MacroFlavors.ForEach(flavor => flavors.Add(flavor));
            }
            return flavors.ToList();
        }
        private List<IntermediateMacroDefinition> PreCompile(List<RawMacroDefinition> defs)
        {
            List<IntermediateMacroDefinition> result = new List<IntermediateMacroDefinition>();
            foreach(var def in defs)
            {
                List<string> flavorsRaw = new List<string>();
                if(def.Flavor is List<string>)
                {
                    flavorsRaw = (List<string>)def.Flavor;
                }
                else
                {
                    flavorsRaw.Add((string)def.Flavor);
                }
                List<GameFlavor> flavors = new List<GameFlavor>();
                foreach (string flavorStr in flavorsRaw)
                {
                    GameFlavor flavor;
                    // there is no duplicate check, but it might not matter.
                    if(Enum.TryParse<GameFlavor>(flavorStr, out flavor))
                    {
                        flavors.Add(flavor);
                    }
                    else
                    {
                        throw new Exception($"Game flavor: {flavorStr}, does not exist.");
                    }
                }
                List<StepDefinition> steps = new List<StepDefinition>();
                List<string> dependencies = new List<string>();
                foreach(object step in def.Steps)
                {
                    if(step is Dictionary<object, object> macroRaw)
                    {
                        string macro = (string)macroRaw["macro"];
                        dependencies.Add(macro);
                        steps.Add(new StepDefinition(macro));
                    }
                    else if(step is List<object> stepRaw)
                    {
                        List<string> castedSteps = new List<string>();
                        foreach(var objStep in stepRaw)
                        {
                            castedSteps.Add((string)objStep);
                        }
                        steps.Add(new StepDefinition(castedSteps));
                    }else if(step is string singleStepRaw)
                    {
                        steps.Add(new StepDefinition(new List<string>() { singleStepRaw }));
                    }
                    else
                    {
                        throw new Exception($"Cannot deserialize step: {step}");
                    }
                }
                IntermediateMacroDefinition newDef = new IntermediateMacroDefinition()
                {
                    Name = def.Name,
                    Steps = steps,
                    Dependencies = dependencies,
                    MacroFlavors = flavors
                };
                result.Add(newDef);
            }
            return result;
        }

        private List<RawMacroDefinition> DeserializeAllFiles(IEnumerable<string> files)
        {
            List<RawMacroDefinition> result = new List<RawMacroDefinition>();
            IDeserializer _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            foreach(string file in files)
            {
                string yaml = File.ReadAllText(file);
                var macro = _deserializer.Deserialize<RawMacroDefinition>(yaml);
                result.Add(macro);
            }
            return result;
        }

        public string GetMacro(GameFlavor flavor, string macro)
        {
            return macros[flavor][macro].Macro;
        }
    }
}
