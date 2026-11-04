using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>What a candidate is for in the hardpoint / ship-construction decision (U.SPC-T378, part of U.SPC-T371).</summary>
    public enum CandidateRole
    {
        FlightAndWeapons,
        HardpointBuilding,
        StaticStructures,
    }

    /// <summary>How sure the project is that a candidate runs on a given Unity / render pipeline.</summary>
    public enum Compatibility
    {
        /// <summary>Nobody has opened the package in this project yet; the store page's claim is all there is.</summary>
        Unchecked,
        Works,
        Fails,
    }

    /// <summary>
    /// One Asset Store package under evaluation. The demo scene is where a showcase goes once the package is
    /// bought and imported; the menu shows the candidate as not installed until that scene is in the build.
    /// </summary>
    public sealed class AssetCandidate
    {
        public string Id { get; }
        public string Name { get; }
        public CandidateRole Role { get; }
        public string Purpose { get; }

        /// <summary>The scene the in-game demo loads, by project path.</summary>
        public string DemoScenePath { get; }

        public Compatibility Unity6 { get; }
        public Compatibility Urp { get; }
        public Compatibility Hdrp { get; }

        public AssetCandidate(string id, string name, CandidateRole role, string purpose,
            Compatibility unity6 = Compatibility.Unchecked, Compatibility urp = Compatibility.Unchecked, Compatibility hdrp = Compatibility.Unchecked)
        {
            Id = id;
            Name = name;
            Role = role;
            Purpose = purpose;
            DemoScenePath = "Assets/AssetLibraryPoc/" + id + "/" + id + "Demo.unity";
            Unity6 = unity6;
            Urp = urp;
            Hdrp = hdrp;
        }

        /// <summary>True once the candidate has been checked on every one of Unity 6, URP and HDRP, which is the
        /// bar before anyone buys it for real.</summary>
        public bool FullyChecked => Unity6 != Compatibility.Unchecked && Urp != Compatibility.Unchecked && Hdrp != Compatibility.Unchecked;

        public static string Describe(Compatibility c) => c == Compatibility.Works ? "works" : c == Compatibility.Fails ? "fails" : "not yet checked";
    }

    /// <summary>The leading Asset Store candidates the proof-of-concept submenu lists.</summary>
    public static class AssetLibraryCandidates
    {
        public static readonly IReadOnlyList<AssetCandidate> All = new[]
        {
            new AssetCandidate("SpaceCombatKit", "Space Combat Kit", CandidateRole.FlightAndWeapons,
                "Ship flight model and weapons."),
            new AssetCandidate("IngameSpaceshipBuilder", "Ingame Spaceship Builder", CandidateRole.HardpointBuilding,
                "Snap-together ship building with hardpoints."),
            new AssetCandidate("ModularStarshipConstructor", "Modular Starship Constructor", CandidateRole.HardpointBuilding,
                "Alternative to Ingame Spaceship Builder for snap-together hardpoint building."),
            new AssetCandidate("SpaceSatelliteKit", "Space Satellite Kit", CandidateRole.StaticStructures,
                "Satellites as static structures."),
            new AssetCandidate("ModularSpaceStation", "Modular Space Station", CandidateRole.StaticStructures,
                "Space stations as static structures."),
        };

        /// <summary>The candidates for one role.</summary>
        public static IEnumerable<AssetCandidate> For(CandidateRole role)
        {
            foreach (var c in All) if (c.Role == role) yield return c;
        }
    }
}
