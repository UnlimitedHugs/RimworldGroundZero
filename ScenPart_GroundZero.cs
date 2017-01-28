using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GroundZero {
	public class ScenPart_GroundZero : ScenPart {
		private const float WoodBreakEfficiency = .1f;

		public int resourceReturn = 100;
		public bool keepBodies = true;
		public bool killPlants = true;
		public bool stoneTerrainOnly = true;
		public bool forbidEverything = true;
		public bool treesDropWood;
		private string resourceReturnBuf;

		public override void PostMapGenerate(Map map) {
			try {
				BreakAllTrees(map);
				if (killPlants) {
					KillAllPlants(map);
				}
				if (stoneTerrainOnly) {
					ReplaceMapTerrain(map);
				}
				BreakMineableThings(map);
				DeconstructMapBuildings(map);
				if (!keepBodies) {
					CleanUpBodies(map);
				}
				if (forbidEverything) {
					ForbidEverything(map);
				}
				map.fogGrid.ClearAllFog();
				map.roofGrid = new RoofGrid(map);
			} catch (Exception e) {
				Log.Warning("Exception during ScenPart_GroundZero.PostMapGenerate: " + e);
			}
		}

		public override void PostGameStart() {
			// wait for the map drawer to be initialized
			LongEventHandler.QueueLongEvent(KillNonColonistPawns, null, false, null);
		}

		public override void DoEditInterface(Listing_ScenEdit listing) {
			var scenPartRect = listing.GetScenPartRect(this, RowHeight * 6);
			Widgets.TextFieldNumericLabeled(new Rect(scenPartRect.x, scenPartRect.y, scenPartRect.width, RowHeight), "Resource yield % ", ref resourceReturn, ref resourceReturnBuf, 0, 100);
			Widgets.CheckboxLabeled(new Rect(scenPartRect.x, scenPartRect.y + RowHeight, scenPartRect.width, RowHeight), "Keep bodies", ref keepBodies);
			Widgets.CheckboxLabeled(new Rect(scenPartRect.x, scenPartRect.y + RowHeight * 2, scenPartRect.width, RowHeight), "Kill plants", ref killPlants);
			Widgets.CheckboxLabeled(new Rect(scenPartRect.x, scenPartRect.y + RowHeight * 3, scenPartRect.width, RowHeight), "Stone terrain only", ref stoneTerrainOnly);
			Widgets.CheckboxLabeled(new Rect(scenPartRect.x, scenPartRect.y + RowHeight * 4, scenPartRect.width, RowHeight), "Forbid everything", ref forbidEverything);
			Widgets.CheckboxLabeled(new Rect(scenPartRect.x, scenPartRect.y + RowHeight * 5, scenPartRect.width, RowHeight), "Trees leave wood", ref treesDropWood);
		}

		private void KillNonColonistPawns() {
			var map = Find.VisibleMap;
			var allThings = map.listerThings.AllThings.ToArray(); // request pawns as things to include shrine mechanoids
			var dinfo = new DamageInfo(DamageDefOf.Bomb, 9999, -1f);
			dinfo.SetBodyRegion(BodyPartHeight.Middle, BodyPartDepth.Outside);
			foreach (var thing in allThings) {
				var pawn = thing as Pawn;
				if (pawn != null && !pawn.Dead) {
					if (pawn.Faction != Faction.OfPlayer) {
						if (keepBodies) {
							pawn.TakeDamage(dinfo);
						} else {
							pawn.Destroy();
						}
					}
				}
			}
		}

		private void CleanUpBodies(Map map) {
			// pods can contain already dead pawns
			var allThings = map.listerThings.AllThings.ToArray();
			foreach (var thing in allThings) {
				if (thing is Corpse) {
					thing.Destroy();
				}
			}
		}

		private void DamageResourceHolder(Thing thing, float extraMultiplier = 1) {
			var damage = thing.MaxHitPoints * (1 - (resourceReturn / 100f) * extraMultiplier);
			thing.TakeDamage(new DamageInfo(DamageDefOf.Bomb, (int)damage, -1F));
		}

		private void BreakAllTrees(Map map) {
			var trees = map.listerThings.AllThings.Where(t => t.def != null && t.def.plant != null && t.def.plant.IsTree).ToArray();
			foreach (var thing in trees) {
				DamageResourceHolder(thing, WoodBreakEfficiency);
				var plant = thing as Plant;
				if (!thing.Destroyed) {
					if (plant != null && treesDropWood) {
						var yeild = plant.YieldNow();
						plant.PlantCollected();
						if (yeild > 0) {
							var wood = ThingMaker.MakeThing(thing.def.plant.harvestedThingDef);
							wood.stackCount = yeild;
							GenPlace.TryPlaceThing(wood, thing.Position, map, ThingPlaceMode.Direct);
						}
					} else {
						thing.Destroy(DestroyMode.Kill);
					}
				}
			}
		}

		private void KillAllPlants(Map map) {
			var plants = map.listerThings.AllThings.Where(t => t.def != null && t.def.plant != null).ToArray();
			foreach (var thing in plants) {
				DamageResourceHolder(thing);
				if (!thing.Destroyed) {
					thing.Destroy(DestroyMode.Kill);
				}
			}
		}

		private void ReplaceMapTerrain(Map map) {
			var baseRockType = Find.World.NaturalRockTypesIn(Find.GameInitData.startingTile).FirstOrDefault(t => t.naturalTerrain != null);
			if (baseRockType != null) {
				map.terrainGrid.ResetGrids();
				for (int i = 0; i < map.terrainGrid.topGrid.Length; i++) {
					map.terrainGrid.topGrid[i] = baseRockType.naturalTerrain;
				}
			}
		}

		private void BreakMineableThings(Map map) {
			var mineables = map.listerThings.AllThings.Where(t => t.def != null && t.def.building != null && t.def.mineable).ToArray();
			foreach (var thing in mineables) {
				DamageResourceHolder(thing);
				if (thing.def.filthLeaving != null) {
					FilthMaker.MakeFilth(thing.Position, map, thing.def.filthLeaving, Rand.RangeInclusive(1, 3));
				}
				if (thing.def.leaveTerrain != null) {
					map.terrainGrid.SetTerrain(thing.Position, thing.def.leaveTerrain);
				}
				if (!thing.Destroyed) {
					TryDropResourceFromMineable(thing);
					thing.Destroy();
				}
			}
		}

		// drops resources in direct proportion to hitpoints remaining
		private void TryDropResourceFromMineable(Thing thing) {
			var thingDef = thing.def;
			if (thingDef.building.mineableThing != null && Rand.Value < thingDef.building.mineableDropChance) {
				var drop = ThingMaker.MakeThing(thingDef.building.mineableThing);
				if (drop.def.stackLimit == 1) {
					drop.stackCount = 1;
				} else {
					var dropMultiplier = (float)thing.HitPoints / thing.MaxHitPoints;
					drop.stackCount = Mathf.CeilToInt(thingDef.building.mineableYield * dropMultiplier);
				}
				if (drop.stackCount > 0) {
					GenSpawn.Spawn(drop, thing.Position, thing.Map);
				}
			}
		}

		private void ForbidEverything(Map map) {
			var things = map.listerThings.AllThings.ToArray();
			foreach (var thing in things) {
				thing.SetForbidden(true, false);
			}
		}

		private void DeconstructMapBuildings(Map map) {
			var deconstructibles = map.listerThings.AllThings.Where(t => t.def != null && t.def.building != null && t.def.building.IsDeconstructible).ToArray();
			foreach (var thing in deconstructibles) {
				DamageResourceHolder(thing);
				try {
					if (!thing.Destroyed) {
						thing.Destroy(DestroyMode.Deconstruct);
					}
				} catch (Exception e) {
					Log.Warning("Minor exception during ScenPart_GroundZero.DeconstructMapBuildings: " + e);
				}
			}
		}
	}
}
