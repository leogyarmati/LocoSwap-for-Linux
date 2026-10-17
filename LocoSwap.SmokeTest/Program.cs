// Headless end-to-end check of the LocoSwap core on Linux.
// Builds a fake Train Simulator install (with deliberately lower-cased directories to mimic
// case differences) and a fake "serz.exe" (a shell snippet that copies .bin <-> .xml), then
// runs the real load -> swap -> save pipeline.
using LocoSwap.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace LocoSwap.SmokeTest
{
    static class Program
    {
        static int failures = 0;
        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) failures++;
        }

        const string D = "xmlns:d=\"http://www.kuju.com/TnT/2003/Delta\"";
        static string Loc(string text, string key) =>
            "<Localisation-cUserLocalisedString><English>" + text + "</English><French/><Italian/><German/><Spanish/><Dutch/><Polish/><Russian/><Other/><Key>" + key + "</Key></Localisation-cUserLocalisedString>";

        static string ScenarioProps(string name, string locoBp) => $@"<cScenarioProperties {D}>
<DisplayName>{Loc(name, "k1")}</DisplayName>
<Description>{Loc("desc", "k2")}</Description>
<DurationMins>30</DurationMins><Author>me</Author><Season>SEASON_SUMMER</Season><StartTime>36000.0</StartTime>
<ScenarioClass>eStandardScenarioClass</ScenarioClass>
<FrontEndDriverList><sDriverFrontEndDetails>
 <ServiceName>{Loc("Svc", "svckey")}</ServiceName><PlayerDriver>1</PlayerDriver>
 <LocoName>{Loc("Old Loco", "lk")}</LocoName>
 <LocoBP><iBlueprintLibrary-cAbsoluteBlueprintID><BlueprintSetID><iBlueprintLibrary-cBlueprintSetID><Provider>ProviderX</Provider><Product>ProductY</Product></iBlueprintLibrary-cBlueprintSetID></BlueprintSetID><BlueprintID>{locoBp}</BlueprintID></iBlueprintLibrary-cAbsoluteBlueprintID></LocoBP>
 <LocoAuthor>ProviderX</LocoAuthor><FilePath>x</FilePath>
</sDriverFrontEndDetails></FrontEndDriverList>
<RBlueprintSetPreLoad/>
</cScenarioProperties>";

        static string ScenarioBin(string bp) => $@"<cRecordSet {D}><Record><cConsist>
<RailVehicles><cOwnedEntity d:id=""1""><Component>
  <cEngine d:id=""2""><UniqueNumber>123</UniqueNumber><Flipped>0</Flipped><Followers/><DisabledEngine>0</DisabledEngine><AWSTimer>0</AWSTimer><AWSExpired>0</AWSExpired><TPWSDistance>0</TPWSDistance>
    <Position>0</Position><Position>20</Position></cEngine>
  <cEngineSimContainer d:id=""3""/>
  <cEntityContainer><StaticChildrenMatrix><e>1</e></StaticChildrenMatrix></cEntityContainer>
 </Component>
 <BlueprintID><iBlueprintLibrary-cAbsoluteBlueprintID><BlueprintSetID><iBlueprintLibrary-cBlueprintSetID><Provider>ProviderX</Provider><Product>ProductY</Product></iBlueprintLibrary-cBlueprintSetID></BlueprintSetID><BlueprintID>{bp}</BlueprintID></iBlueprintLibrary-cAbsoluteBlueprintID></BlueprintID>
 <ReskinBlueprintID><iBlueprintLibrary-cAbsoluteBlueprintID><BlueprintSetID><iBlueprintLibrary-cBlueprintSetID><Provider/><Product/></iBlueprintLibrary-cBlueprintSetID></BlueprintSetID><BlueprintID/></iBlueprintLibrary-cAbsoluteBlueprintID></ReskinBlueprintID>
 <Name>OldLoco</Name></cOwnedEntity></RailVehicles>
<Driver><cDriver><ServiceName>{Loc("Svc", "svckey")}</ServiceName><PlayerDriver>1</PlayerDriver><InitialRV><e>123</e></InitialRV></cDriver></Driver>
</cConsist></Record></cRecordSet>";

        static string VehicleBp(string name, string numbersCsv) => $@"<cBlueprint {D}><cEngineBlueprint><Name>{name}</Name>
<DisplayName>{Loc(name + " display", "vk")}</DisplayName>
<CollisionLength d:type=""sFloat32"">19.5</CollisionLength>
<cEntityContainerBlueprint-sChild/><cEntityContainerBlueprint-sChild/>
<NumberingList><cCSVContainer><CsvFile>{numbersCsv}</CsvFile></cCSVContainer></NumberingList>
</cEngineBlueprint></cBlueprint>";

        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "locoswap-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
            Log.Echo = false;

            string ts = Path.Combine(root, "RailWorks");
            Write(Path.Combine(ts, "railworks.exe"), "");            // lower-case on purpose
            Write(Path.Combine(ts, "serz.exe"), "");
            Settings.Override(new Settings { TsPath = ts, Language = "en", ScenarioNameSuffix = "[LoSw]" });
            Check(Utilities.IsValidTsPath(ts), "TS path accepted although RailWorks.exe is spelled railworks.exe");

            // Fake serz: X.bin -> X.xml, X.xml -> X.bin
            SerzRunner.StartInfoFactory = file =>
            {
                var psi = new ProcessStartInfo("/bin/sh");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("case \"$1\" in *.bin) cp \"$1\" \"${1%.bin}.xml\";; *.xml) cp \"$1\" \"${1%.xml}.bin\";; esac; echo converted $1");
                psi.ArgumentList.Add("sh");
                psi.ArgumentList.Add(file);
                return psi;
            };

            // ---- routes ----
            string routes = Path.Combine(ts, "Content", "Routes");
            string routeA = "11111111-1111-1111-1111-111111111111";
            string routeB = "22222222-2222-2222-2222-222222222222";
            Write(Path.Combine(routes, routeA, "RouteProperties.xml"), $"<cRouteProperties {D}><DisplayName>{Loc("Route Alpha", "r1")}</DisplayName></cRouteProperties>");
            Directory.CreateDirectory(Path.Combine(routes, routeB));
            string scnA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            string scnB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
            using (var zip = ZipFile.Open(Path.Combine(routes, routeB, "content.ap"), ZipArchiveMode.Create))
            {
                void Add(string name, string text) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open()); w.Write(text); }
                Add("RouteProperties.xml", $"<cRouteProperties {D}><DisplayName>{Loc("Route Beta (in .ap)", "r2")}</DisplayName></cRouteProperties>");
                Add($"Scenarios/{scnB}/ScenarioProperties.xml", ScenarioProps("Beta scenario", @"RailVehicles\Loco\Old.xml"));
                Add($"Scenarios/{scnB}/Scenario.bin", ScenarioBin(@"RailVehicles\Loco\Old.xml"));
            }

            var found = Route.ListAllRoutes();
            Check(found.Length == 2, "two routes listed (got " + found.Length + ")");
            Check(found.Any(r => r.Name == "Route Alpha"), "route read from RouteProperties.xml");
            Check(found.Any(r => r.Name == "Route Beta (in .ap)"), "route read from inside .ap");

            // ---- a loose scenario in route A ----
            string scnDir = Path.Combine(routes, routeA, "Scenarios", scnA);
            Write(Path.Combine(scnDir, "ScenarioProperties.xml"), ScenarioProps("Alpha scenario", @"RailVehicles\Loco\Old.xml"));
            Write(Path.Combine(scnDir, "Scenario.bin"), ScenarioBin(@"RailVehicles\Loco\Old.xml"));
            var rA = new Route(routeA);
            var scenario = new Scenario(rA, scnA, "");
            Check(scenario.Name == "Alpha scenario" && scenario.Duration == 30 && scenario.Season == Scenario.Seasons.Summer, "scenario properties parsed");
            Check(scenario.StartTimeText == "10:00", "start time formatted (" + scenario.StartTimeText + ")");

            // ---- fake game assets; directories deliberately lower-case ----
            string assets = Path.Combine(ts, "Assets", "providerx", "producty", "railvehicles");
            Write(Path.Combine(assets, "loco", "New.bin"), VehicleBp("NewLoco", @"ProviderX\ProductY\RailVehicles\Numbers"));
            Write(Path.Combine(assets, "numbers.dcsv"), $"<cCSV {D}><cCSVItem><Name>9001</Name></cCSVItem><cCSVItem><Name>9002</Name></cCSVItem></cCSV>");
            using (var zip = ZipFile.Open(Path.Combine(ts, "Assets", "providerx", "producty", "pack.ap"), ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("RailVehicles/Wagon/Wag.bin");
                using var w = new StreamWriter(e.Open()); w.Write(VehicleBp("WagInAp", @"ProviderX\ProductY\RailVehicles\Numbers"));
            }

            scenario.ReadScenario();
            var consists = scenario.GetConsists();
            Check(consists.Count == 1 && consists[0].Vehicles.Count == 1, "one consist with one vehicle");
            var oldVeh = consists[0].Vehicles[0];
            Check(oldVeh.Exists == VehicleExistance.Missing, "old vehicle reported missing");
            Check(Math.Abs(oldVeh.Length - 20) < 0.01 && oldVeh.Number == "123" && consists[0].IsPlayerConsist, "length/number/player flag parsed");
            Check(oldVeh.ListLabel == "OldLoco - 20m", "list label: " + oldVeh.ListLabel);

            // ---- available vehicles (case-insensitive path + .ap) ----
            var newVeh = new AvailableVehicle(@"ProviderX\ProductY\RailVehicles\Loco\New.bin");
            Check(newVeh.Name == "NewLoco" && newVeh.DisplayName == "NewLoco display", "loose vehicle loaded through wrong-case dirs");
            Check(Math.Abs(newVeh.Length - 19.5f) < 0.01 && newVeh.EntityCount == 2 && newVeh.Type == VehicleType.Engine, "vehicle length/entities/type");
            Check(newVeh.NumberingList.Count == 2 && newVeh.NumberingList[0] == "9001", "numbering list read (wrong-case .dcsv)");
            var wag = new AvailableVehicle(@"ProviderX\ProductY\RailVehicles\Wagon\Wag.bin");
            Check(wag.Name == "WagInAp", "vehicle loaded from inside .ap");

            // ---- swap + save ----
            Settings.Default.Preset.List.Add(new SwapPresetItem { TargetXmlPath = oldVeh.XmlPath, TargetName = "OldLoco", NewName = "x", NewXmlPath = "x" });
            oldVeh.CopyFrom(newVeh);
            scenario.ReplaceVehicle(consists[0].Idx, oldVeh.Idx, newVeh);
            scenario.ChangeVehicleNumber(consists[0].Idx, oldVeh.Idx, oldVeh.Number);
            consists[0].DetermineCompletenessAfterReplace();
            Check(consists[0].IsComplete == ConsistVehicleExistance.FullyReplaced, "consist marked fully replaced");
            Check(oldVeh.Number == "9001" || oldVeh.Number == "9002", "new number picked from numbering list (" + oldVeh.Number + ")");

            scenario.Save();
            var savedBin = XDocument.Load(Path.Combine(scnDir, "Scenario.bin"));
            Check(savedBin.Descendants("BlueprintID").Any(e => e.Value == newVeh.BlueprintId), "Scenario.bin contains new blueprint (" + newVeh.BlueprintId + ")");
            Check(savedBin.Descendants("UniqueNumber").First().Value == oldVeh.Number, "unique number written");
            Check(savedBin.Descendants("InitialRV").Elements("e").First().Value == oldVeh.Number, "InitialRV updated");
            var savedProps = XDocument.Load(Path.Combine(scnDir, "ScenarioProperties.xml"));
            Check(savedProps.Descendants("DisplayName").Descendants("English").First().Value.EndsWith("[LoSw]"), "name suffix appended");
            Check(Directory.GetFiles(scnDir, "ScenarioBackup-*.bin").Length == 1, "backup of Scenario.bin created");

            // ---- scenario inside an .ap: load, then save extracts it ----
            var rB = new Route(routeB);
            var scenarioB = new Scenario(rB, scnB, Path.Combine(routes, routeB, "content.ap"));
            Check(scenarioB.Name == "Beta scenario", "scenario read from .ap");
            scenarioB.ReadScenario();
            scenarioB.GetConsists();
            scenarioB.Save();
            Check(File.Exists(Path.Combine(routes, routeB, "Scenarios", scnB, "Scenario.bin")), "saving an .ap scenario extracts it next to the route");

            // ---- SDBCache ----
            Write(Path.Combine(ts, "Content", "SDBCache.bin"),
                $"<root><sSDScenario><A><DevString>{routeA}</DevString></A><B><DevString>{scnA}</DevString></B><Completion>CompletedSuccessfully</Completion></sSDScenario></root>");
            ScenarioDb.ParseScenarioDb();
            Check(ScenarioDb.dbState == ScenarioDb.DBState.Loaded, "SDBCache parsed");
            Check(ScenarioDb.getScenarioDbInfos(routeA, scnA) == ScenarioDb.ScenarioCompletion.CompletedSuccessfully, "completion status looked up");

            // ---- archive toggle ----
            rA.ToggleArchive();
            Check(rA.IsArchived && File.Exists(Path.Combine(routes, routeA, "RouteProperties.xml.LSoff")), "route archived");
            rA.ToggleArchive();
            Check(!rA.IsArchived && File.Exists(Path.Combine(routes, routeA, "RouteProperties.xml")), "route un-archived");

            // ---- misc ----
            Check(SerzRunner.Tokenize("\"/opt/my proton/proton\" run").SequenceEqual(new[] { "/opt/my proton/proton", "run" }), "runner command tokenizer");
            Check(Fs.ToWinePath("/home/u/x.bin") == "Z:\\home\\u\\x.bin", "wine path conversion");

            Settings.Default.Language = "de"; Utilities.ApplyLanguage();
            Check(LocoSwap.Language.Resources.yes == "Ja", "German resources load (yes = " + LocoSwap.Language.Resources.yes + ")");
            Settings.Default.Language = "en"; Utilities.ApplyLanguage();

            // settings round trip
            Settings.Default.FavoriteRoutes.Add(routeA);
            Settings.Default.Save();
            Settings.Override(null);
            Check(Settings.Default.FavoriteRoutes.Contains(routeA) && Settings.Default.Preset.List.Count == 1 && Settings.Default.TsPath == ts, "settings JSON round trip");

            try { Directory.Delete(root, true); } catch (Exception) { }
            Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : "\n" + failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
