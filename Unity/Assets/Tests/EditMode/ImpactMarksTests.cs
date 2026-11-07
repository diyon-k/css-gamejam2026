using Blink.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class ImpactMarksTests
    {
        [Test]
        public void FromName_MapsHouseMaterials_AndDefaultsToPlaster()
        {
            Assert.AreEqual(SurfaceKind.Wood, SurfaceTable.FromName("DarkWood (Instance)"));
            Assert.AreEqual(SurfaceKind.Plaster, SurfaceTable.FromName("GrimyPlaster"));
            Assert.AreEqual(SurfaceKind.Stone, SurfaceTable.FromName("FireplaceStone"));
            Assert.AreEqual(SurfaceKind.Metal, SurfaceTable.FromName("CastIron"));
            Assert.AreEqual(SurfaceKind.Glass, SurfaceTable.FromName("GlassLit"));
            Assert.AreEqual(SurfaceKind.Fabric, SurfaceTable.FromName("Rug_Oxblood"));
            Assert.AreEqual(SurfaceKind.Paper, SurfaceTable.FromName("Book_Red"));
            Assert.AreEqual(SurfaceKind.Water, SurfaceTable.FromName("FloodWater"));
            Assert.AreEqual(SurfaceKind.Porcelain, SurfaceTable.FromName("Porcelain"));
            Assert.AreEqual(SurfaceKind.Wood, SurfaceTable.FromName("PanelDoor_Tall"));
            Assert.AreEqual(SurfaceKind.Plaster, SurfaceTable.FromName("SomethingNew"));
            Assert.AreEqual(SurfaceKind.Plaster, SurfaceTable.FromName(null));
        }

        [Test]
        public void Marks_FollowTheDocTable()
        {
            Assert.AreEqual(MarkCell.BulletWood, SurfaceTable.BulletMark(SurfaceKind.Wood));
            Assert.IsNull(SurfaceTable.BulletMark(SurfaceKind.Water));
            Assert.AreEqual(MarkCell.GashWood, SurfaceTable.MeleeMark(SurfaceKind.Wood, MeleeTool.Blade));
            Assert.AreEqual(MarkCell.Scrape, SurfaceTable.MeleeMark(SurfaceKind.Wood, MeleeTool.Prying));
            Assert.AreEqual(MarkCell.Dent, SurfaceTable.MeleeMark(SurfaceKind.Metal, MeleeTool.Blunt));
            Assert.AreEqual(MarkCell.Crack, SurfaceTable.MeleeMark(SurfaceKind.Plaster, MeleeTool.Blunt));
            Assert.IsNull(SurfaceTable.MeleeMark(SurfaceKind.Fabric, MeleeTool.Blunt));
            Assert.AreEqual(MarkCell.FabricTear, SurfaceTable.MeleeMark(SurfaceKind.Fabric, MeleeTool.Blade));
        }

        [Test]
        public void UvBias_CellZeroIsTopLeft_LastIsBottomRight()
        {
            Assert.AreEqual(new Vector2(0f, 0.75f), SurfaceTable.UvBias(MarkCell.BulletWood));
            Assert.AreEqual(new Vector2(0.75f, 0.75f), SurfaceTable.UvBias(MarkCell.BulletMetal));
            Assert.AreEqual(new Vector2(0f, 0.5f), SurfaceTable.UvBias(MarkCell.GashWood));
            Assert.AreEqual(new Vector2(0.75f, 0f), SurfaceTable.UvBias(MarkCell.Scorch));
        }
    }
}
