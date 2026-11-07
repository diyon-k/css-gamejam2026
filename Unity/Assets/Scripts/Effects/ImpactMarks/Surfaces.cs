using System;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>What a world surface is made of, for marks, particles and sounds.</summary>
    public enum SurfaceKind
    {
        Plaster,
        Wood,
        Stone,
        Metal,
        Glass,
        Fabric,
        Porcelain,
        Paper,
        Water,
    }

    /// <summary>
    /// Cells of the impact atlas (<c>Resources/ImpactMarks/ImpactAtlas.png</c>, 4x4). Cell 0 is the top-left of the
    /// image, row-major. Keep in step with <c>tools/impact_atlas.py</c>.
    /// </summary>
    public enum MarkCell
    {
        BulletWood, BulletPlaster, BulletStone, BulletMetal,
        GashWood, GashPlaster, Dent, Scrape,
        Crack, GlassCrack, FabricTear, PaperHole,
        Blood1, Blood2, BloodPool, Scorch,
    }

    /// <summary>Overrides the material-name lookup for one object (and its children).</summary>
    [DisallowMultipleComponent]
    public sealed class Surface : MonoBehaviour
    {
        [SerializeField] SurfaceKind kind;
        public SurfaceKind Kind => kind;
    }

    /// <summary>Pure lookups: material name to surface, and which mark a hit leaves.</summary>
    public static class SurfaceTable
    {
        // First match wins, so the more specific names go first.
        static readonly (string token, SurfaceKind kind)[] Tokens =
        {
            ("glass", SurfaceKind.Glass), ("bulb", SurfaceKind.Glass),
            ("water", SurfaceKind.Water),
            ("porcelain", SurfaceKind.Porcelain), ("ceramic", SurfaceKind.Porcelain), ("plate", SurfaceKind.Porcelain),
            ("book", SurfaceKind.Paper), ("paper", SurfaceKind.Paper), ("canvas", SurfaceKind.Paper),
            ("rug", SurfaceKind.Fabric), ("fabric", SurfaceKind.Fabric), ("dustsheet", SurfaceKind.Fabric),
            ("lampshade", SurfaceKind.Fabric), ("cloth", SurfaceKind.Fabric), ("leather", SurfaceKind.Fabric),
            ("linen", SurfaceKind.Fabric), ("sofa", SurfaceKind.Fabric), ("armchair", SurfaceKind.Fabric), ("bed", SurfaceKind.Fabric),
            ("iron", SurfaceKind.Metal), ("brass", SurfaceKind.Metal), ("rust", SurfaceKind.Metal), ("silver", SurfaceKind.Metal),
            ("steel", SurfaceKind.Metal), ("cutlery", SurfaceKind.Metal), ("metal", SurfaceKind.Metal), ("chandelier", SurfaceKind.Metal), ("candelabra", SurfaceKind.Metal),
            ("stone", SurfaceKind.Stone), ("concrete", SurfaceKind.Stone), ("brick", SurfaceKind.Stone), ("marble", SurfaceKind.Stone),
            ("wood", SurfaceKind.Wood), ("lacquer", SurfaceKind.Wood), ("table", SurfaceKind.Wood), ("chair", SurfaceKind.Wood),
            ("shelf", SurfaceKind.Wood), ("shelves", SurfaceKind.Wood), ("cabinet", SurfaceKind.Wood), ("commode", SurfaceKind.Wood),
            ("nightstand", SurfaceKind.Wood), ("crate", SurfaceKind.Wood), ("barrel", SurfaceKind.Wood), ("clock", SurfaceKind.Wood),
            ("door", SurfaceKind.Wood), ("wardrobe", SurfaceKind.Wood), ("drawers", SurfaceKind.Wood), ("piano", SurfaceKind.Wood),
            ("plaster", SurfaceKind.Plaster),
        };

        /// <summary>Surface for a material name ("DarkWood (Instance)" works). Unknown names are plaster.</summary>
        public static SurfaceKind FromName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return SurfaceKind.Plaster;
            foreach (var (token, kind) in Tokens)
                if (materialName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return kind;
            return SurfaceKind.Plaster;
        }

        /// <summary>Surface of a hit collider: a <see cref="Surface"/> override, else its renderer's first material.</summary>
        public static SurfaceKind Of(Collider collider)
        {
            if (collider == null) return SurfaceKind.Plaster;
            var over = collider.GetComponentInParent<Surface>();
            if (over != null) return over.Kind;
            var r = collider.GetComponent<Renderer>();
            if (r == null) r = collider.GetComponentInParent<Renderer>();
            if (r == null) r = collider.GetComponentInChildren<Renderer>();
            var mat = r != null ? r.sharedMaterial : null;
            return FromName(mat != null ? mat.name : collider.name);
        }

        /// <summary>The mark a bullet leaves, or null for none (water).</summary>
        public static MarkCell? BulletMark(SurfaceKind s) => s switch
        {
            SurfaceKind.Wood => MarkCell.BulletWood,
            SurfaceKind.Stone => MarkCell.BulletStone,
            SurfaceKind.Metal => MarkCell.BulletMetal,
            SurfaceKind.Glass => MarkCell.GlassCrack,
            SurfaceKind.Fabric => MarkCell.FabricTear,
            SurfaceKind.Porcelain => MarkCell.Crack,
            SurfaceKind.Paper => MarkCell.PaperHole,
            SurfaceKind.Water => null,
            _ => MarkCell.BulletPlaster,
        };

        /// <summary>The mark a melee blow leaves, or null for none.</summary>
        public static MarkCell? MeleeMark(SurfaceKind s, MeleeTool tool) => (s, tool) switch
        {
            (SurfaceKind.Water, _) => null,
            (SurfaceKind.Glass, _) => MarkCell.GlassCrack,
            (SurfaceKind.Fabric or SurfaceKind.Paper, MeleeTool.Blade) => MarkCell.FabricTear,
            (SurfaceKind.Fabric or SurfaceKind.Paper, _) => null,
            (SurfaceKind.Wood, MeleeTool.Blade) => MarkCell.GashWood,
            (SurfaceKind.Plaster, MeleeTool.Blade) => MarkCell.GashPlaster,
            (SurfaceKind.Plaster, MeleeTool.Blunt) => MarkCell.Crack,
            (SurfaceKind.Porcelain, _) => MarkCell.Crack,
            (_, MeleeTool.Blunt) => MarkCell.Dent,
            _ => MarkCell.Scrape,
        };

        /// <summary>Atlas cell to DecalProjector uvBias (uvScale is 0.25, 0.25). Cell 0 is the image's top-left.</summary>
        public static Vector2 UvBias(MarkCell cell)
        {
            int i = (int)cell;
            return new Vector2(i % 4 * 0.25f, (3 - i / 4) * 0.25f);
        }
    }

    /// <summary>How a melee item marks a surface.</summary>
    public enum MeleeTool
    {
        /// <summary>The axe: gashes.</summary>
        Blade,
        /// <summary>Crowbar, wrench, poker: long scrapes.</summary>
        Prying,
        /// <summary>Pan, plank, candlestick: round dents.</summary>
        Blunt,
    }
}
