using System.Collections.Generic;
using Cardio.Core;
using Cardio.Gameplay;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cardio.EditorTools
{
    /// <summary>References the scene generator needs after an environment is built.</summary>
    public struct LevelEnvironment
    {
        public GameObject Root;
        public Transform SpawnPoint;
        public Transform ExitAnchor;
    }

    /// <summary>
    /// Procedural greybox environments built from voxel blocks.
    ///
    /// Design decisions worth noting in the report:
    ///  * Geometry is generated, not modelled, so the anatomical layout is
    ///    defined in code and can be corrected precisely (the PSM1 requirement
    ///    is a stylised look with an accurate structure).
    ///  * Nothing casts real-time shadows and there is exactly one directional
    ///    light. Combined with flat ambient light and fog, that keeps the
    ///    rendering cost low enough for the 60 FPS laptop target.
    ///  * These are placeholders for the MagicaVoxel assets: replacing a block
    ///    with an imported .vox mesh later does not affect any gameplay script.
    /// </summary>
    public static class EnvironmentFactory
    {
        private const float ChamberRadius = 15f;
        private const float WallHeight = 11f;
        private const int WallSegments = 36;

        // ------------------------------------------------------------------
        // Level 1 - Left Ventricle
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the tutorial chamber.
        ///
        /// Anatomy represented (blood flows in the correct direction):
        ///   mitral valve (inflow, -Z)  ->  left ventricle cavity
        ///   -> aortic valve (outflow, +Z)  ->  ascending aorta  ->  level exit
        /// Papillary muscles sit inside the cavity and the interventricular
        /// septum forms the -X wall, which is where it sits in a real heart.
        /// </summary>
        public static LevelEnvironment BuildLeftVentricle()
        {
            var root = new GameObject("Environment_LeftVentricle");

            GameObject floor = CreateFloorDisc(root.transform, "Floor_Endocardium", Vector3.zero, ChamberRadius * 2f);

            // Wall ring with openings at the two valves (+Z outflow, -Z inflow).
            // 34 degrees of arc at radius 15 clears roughly 7 units, which matches
            // the corridor width so the doorway is not partly blocked.
            BuildRingWall(root.transform, ChamberRadius, WallSegments, WallHeight,
                          new[] { new Vector2(0f, 34f), new Vector2(180f, 34f) });

            // ---- Inflow: mitral valve and the left atrium passage ----
            BuildCorridor(root.transform, "Corridor_MitralInflow", new Vector3(0f, 0f, -20f), new Vector3(7f, 8f, 12f));
            Renderer[] mitralFlaps = BuildValveFlaps(root.transform, "Valve_Mitral", new Vector3(0f, 0f, -15f), 6.5f);

            // ---- Outflow: aortic valve and the ascending aorta ----
            // The corridor is captured so its geometry can be tagged as the aorta,
            // which makes "drag the Aorta label onto the aorta" answerable.
            GameObject aortaCorridor = BuildCorridor(root.transform, "Corridor_Aorta", new Vector3(0f, 0f, 21f), new Vector3(7f, 8f, 14f));
            Renderer[] aorticFlaps = BuildValveFlaps(root.transform, "Valve_Aortic", new Vector3(0f, 0f, 15f), 6.5f);

            // ---- Papillary muscles: the pillars that anchor the mitral valve ----
            var papillaryRenderers = new List<Object>();
            papillaryRenderers.Add(CreateBlock(root.transform, "PapillaryMuscle_Anterior",
                                               new Vector3(-5.5f, 2.5f, -3f), new Vector3(2f, 5f, 2f), ProjectAssets.MuscleWall).GetComponent<Renderer>());
            papillaryRenderers.Add(CreateBlock(root.transform, "PapillaryMuscle_Posterior",
                                               new Vector3(5.5f, 2.5f, -3f), new Vector3(2f, 5f, 2f), ProjectAssets.MuscleWall).GetComponent<Renderer>());

            // ---- Interventricular septum: the muscular wall shared with the right ventricle ----
            GameObject septum = CreateBlock(root.transform, "InterventricularSeptum",
                                            new Vector3(-13.5f, 4f, 0f), new Vector3(2.5f, 8f, 12f), ProjectAssets.MuscleWallDark);

            // ---- A fatty plaque hazard in the aorta, so the player must steer around it ----
            // Offset to +X, leaving a ~2.3 unit gap against the -X corridor wall:
            // wide enough to walk through, tight enough to demand attention.
            BuildPlaqueHazard(root.transform, "Hazard_FattyPlaque_Aorta", new Vector3(1.9f, 0f, 20f), new Vector3(4f, 1.2f, 4f), 10);

            // ---- Educational signage ----
            CreateAnatomyMarker(root.transform, "left_ventricle", "Left Ventricle",
                "The heart's strongest chamber. It pumps oxygenated blood into the aorta and out to the whole body.",
                new Vector3(0f, 6.5f, 0f), 12f, new[] { floor.GetComponent<Renderer>() });

            CreateAnatomyMarker(root.transform, "mitral_valve", "Mitral Valve (Bicuspid)",
                "Two flaps between the left atrium and left ventricle. Stops blood flowing back into the atrium.",
                new Vector3(0f, 5f, -15f), 10f, mitralFlaps);

            CreateAnatomyMarker(root.transform, "aortic_valve", "Aortic Valve",
                "Three cusps at the ventricle outlet. Opens to release blood into the aorta, then seals shut.",
                new Vector3(0f, 5f, 15f), 10f, aorticFlaps);

            CreateAnatomyMarker(root.transform, "papillary_muscle", "Papillary Muscles",
                "Muscular pillars anchoring the mitral valve through the chordae tendineae so it cannot invert.",
                new Vector3(0f, 5.5f, -3f), 9f, papillaryRenderers.ConvertAll(o => (Renderer)o).ToArray());

            CreateAnatomyMarker(root.transform, "interventricular_septum", "Interventricular Septum",
                "The thick muscular wall separating the left and right ventricles.",
                new Vector3(-13.5f, 7f, 0f), 9f, new[] { septum.GetComponent<Renderer>() });

            CreateAnatomyMarker(root.transform, "aorta", "Ascending Aorta",
                "The body's largest artery. It carries oxygenated blood away from the left ventricle.",
                new Vector3(0f, 5.5f, 24f), 10f, aortaCorridor.GetComponentsInChildren<Renderer>());

            // ---- Invisible walls sealing the open ends of both corridors ----
            // The chamber is a closed ring, but each corridor is a tube with one
            // end left open to the void: the inflow floor stops at z = -26 and
            // the aorta floor at z = +28. Without these the player simply walks
            // off the edge. Colliders only - no renderer - so the corridors
            // still read as continuing into the rest of the circulatory system.
            CreateInvisibleBarrier(root.transform, "Barrier_MitralInflowEnd", new Vector3(0f, 4f, -26f), new Vector3(9f, 8f, 0.5f));
            CreateInvisibleBarrier(root.transform, "Barrier_AortaEnd", new Vector3(0f, 4f, 28f), new Vector3(9f, 8f, 0.5f));

            // ---- Anchors used by the scene generator ----
            Transform spawn = CreateAnchor(root.transform, "SpawnPoint", new Vector3(0f, 1.2f, -20f), Quaternion.identity);
            Transform exit = CreateAnchor(root.transform, "ExitAnchor", new Vector3(0f, 1.5f, 26f), Quaternion.identity);

            MarkStatic(root);
            return new LevelEnvironment { Root = root, SpawnPoint = spawn, ExitAnchor = exit };
        }

        // ------------------------------------------------------------------
        // Levels 2 and 3 - honest placeholders
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Level 2 - Cerebral circulation (Phase 8)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the arterial supply to the brain as a branching, maze-like
        /// vessel network.
        ///
        /// Anatomy represented (blood flows in the correct direction):
        ///   vertebral arteries -> basilar artery -------------\
        ///                                                      > CIRCLE OF WILLIS -> cerebral arteries -> exit
        ///   internal carotid arteries -> middle cerebral -----/
        ///
        /// The Circle of Willis is built as a genuine ring because that is what
        /// it is: an anastomosis joining the anterior (carotid) and posterior
        /// (vertebrobasilar) supplies. Its purpose is collateral flow, and this
        /// level teaches that structurally rather than only in text - a
        /// thrombus seals the basilar route, so the only way through is the long
        /// way round via a carotid and across the ring. That is precisely what
        /// the Circle of Willis exists to do, and the same fact the
        /// lv2_mc_collateral and lv2_mc_ischaemic_stroke questions ask about.
        ///
        /// EVERY JUNCTION IS A DISC, never two crossing corridors. BuildCorridor
        /// always emits two full-length side walls, so a corridor crossing
        /// another lays walls straight across it: the first version of this
        /// level sealed its own spawn point inside a wall and left the whole
        /// southern half unreachable. A junction disc is a floor with a ring
        /// wall broken by a gap per corridor, so no wall ever crosses a route.
        /// The A* navigability check asserts the result.
        ///
        /// Corridors are 6 units wide against Level 1's 7, giving the "narrow
        /// paths" the roadmap asks for without pinching the grid shut.
        /// </summary>
        public static LevelEnvironment BuildCerebralVessels()
        {
            var root = new GameObject("Environment_CerebralCirculation");

            const float vesselWidth = 6f;
            const float vesselHeight = 8f;

            // ---- The Circle of Willis: a ring with four openings ----
            GameObject ringFloor = CreateFloorDisc(root.transform, "Floor_CircleOfWillis", Vector3.zero, 20f);
            BuildRingWall(root.transform, 10f, 32, vesselHeight,
                          new[]
                          {
                              new Vector2(0f, 40f),     // +Z  cerebral arteries, to the exit
                              new Vector2(90f, 40f),    // +X  right middle cerebral
                              new Vector2(180f, 40f),   // -Z  basilar inflow
                              new Vector2(270f, 40f)    // -X  left middle cerebral
                          },
                          thickness: 1.8f);

            // ---- Junction discs ----
            BuildJunctionDisc(root.transform, "Junction_VertebralInlet", new Vector3(0f, 0f, -33f), 7f,
                              new[] { new Vector2(0f, 62f), new Vector2(90f, 62f), new Vector2(270f, 62f) });
            BuildJunctionDisc(root.transform, "Junction_CarotidBend_NW", new Vector3(-26f, 0f, -33f), 6f,
                              new[] { new Vector2(0f, 68f), new Vector2(90f, 68f) });
            BuildJunctionDisc(root.transform, "Junction_CarotidBend_NE", new Vector3(26f, 0f, -33f), 6f,
                              new[] { new Vector2(0f, 68f), new Vector2(270f, 68f) });
            BuildJunctionDisc(root.transform, "Junction_CarotidTop_W", new Vector3(-26f, 0f, 0f), 6f,
                              new[] { new Vector2(180f, 68f), new Vector2(90f, 68f) });
            BuildJunctionDisc(root.transform, "Junction_CarotidTop_E", new Vector3(26f, 0f, 0f), 6f,
                              new[] { new Vector2(180f, 68f), new Vector2(270f, 68f) });

            // ---- Posterior supply: vertebrals merging into the basilar ----
            GameObject basilar = BuildCorridor(root.transform, "Corridor_BasilarArtery",
                                               new Vector3(0f, 0f, -18f), new Vector3(vesselWidth, vesselHeight, 19f));

            // ---- Anterior supply: the two internal carotids ----
            GameObject carotidW = BuildCorridor(root.transform, "Corridor_InternalCarotid_W",
                                                new Vector3(-13.5f, 0f, -33f), new Vector3(vesselWidth, vesselHeight, 16f));
            carotidW.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GameObject carotidE = BuildCorridor(root.transform, "Corridor_InternalCarotid_E",
                                                new Vector3(13.5f, 0f, -33f), new Vector3(vesselWidth, vesselHeight, 16f));
            carotidE.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            GameObject carotidRiseW = BuildCorridor(root.transform, "Corridor_CarotidAscending_W",
                                                    new Vector3(-26f, 0f, -16.5f), new Vector3(vesselWidth, vesselHeight, 24f));
            GameObject carotidRiseE = BuildCorridor(root.transform, "Corridor_CarotidAscending_E",
                                                    new Vector3(26f, 0f, -16.5f), new Vector3(vesselWidth, vesselHeight, 24f));

            GameObject mcaW = BuildCorridor(root.transform, "Corridor_MiddleCerebral_W",
                                            new Vector3(-15f, 0f, 0f), new Vector3(vesselWidth, vesselHeight, 13f));
            mcaW.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GameObject mcaE = BuildCorridor(root.transform, "Corridor_MiddleCerebral_E",
                                            new Vector3(15f, 0f, 0f), new Vector3(vesselWidth, vesselHeight, 13f));
            mcaE.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // ---- Outflow: cerebral arteries to the cortex ----
            GameObject cerebral = BuildCorridor(root.transform, "Corridor_CerebralArteries",
                                                new Vector3(0f, 0f, 19.25f), new Vector3(vesselWidth, vesselHeight, 21.5f));

            // ---- Static blockades ----
            // The thrombus fully seals the basilar. That is deliberate and it is
            // the point of the level: the direct route is closed, so the player
            // has to find the collateral path. Both carotids stay open, which
            // the A* navigability check asserts on every run.
            GameObject thrombus = CreateBlock(root.transform, "Blockade_BasilarThrombus",
                                              new Vector3(0f, 2.5f, -18f), new Vector3(vesselWidth, 5f, 2.2f),
                                              ProjectAssets.Plaque);
            int obstacleLayer = LayerMask.NameToLayer(GameConstants.LayerObstacle);
            if (obstacleLayer >= 0) thrombus.layer = obstacleLayer;

            // Chicanes narrowing the ascending carotids without closing them,
            // which is what makes the run read as a vessel rather than a hallway.
            CreateBlock(root.transform, "Blockade_Atheroma_W", new Vector3(-27.4f, 2f, -8f),
                        new Vector3(2.4f, 4f, 3f), ProjectAssets.Plaque);
            CreateBlock(root.transform, "Blockade_Atheroma_E", new Vector3(27.4f, 2f, -24f),
                        new Vector3(2.4f, 4f, 3f), ProjectAssets.Plaque);

            BuildPlaqueHazard(root.transform, "Hazard_Atherosclerosis_Carotid",
                              new Vector3(-24.6f, 0f, -20f), new Vector3(3f, 1.2f, 3f), 10);

            // ---- Educational signage, and the tags that make picking work ----
            CreateAnatomyMarker(root.transform, "circle_of_willis", "Circle of Willis",
                "A ring of arteries at the base of the brain joining the carotid and vertebrobasilar supplies. If one route is blocked, blood can still arrive by another.",
                new Vector3(0f, 6f, 0f), 12f, new[] { ringFloor.GetComponent<Renderer>() });

            CreateAnatomyMarker(root.transform, "basilar_artery", "Basilar Artery",
                "Formed where the two vertebral arteries merge. It supplies the brainstem and cerebellum and feeds the back of the Circle of Willis.",
                new Vector3(0f, 5.5f, -25f), 11f, basilar.GetComponentsInChildren<Renderer>());

            CreateAnatomyMarker(root.transform, "internal_carotid", "Internal Carotid Arteries",
                "The paired anterior supply. Each enters the skull and joins the Circle of Willis, together carrying most of the brain's blood.",
                new Vector3(-26f, 5.5f, -16f), 12f,
                Combine(Combine(carotidW.GetComponentsInChildren<Renderer>(), carotidE.GetComponentsInChildren<Renderer>()),
                        Combine(carotidRiseW.GetComponentsInChildren<Renderer>(), carotidRiseE.GetComponentsInChildren<Renderer>())));

            CreateAnatomyMarker(root.transform, "middle_cerebral_artery", "Middle Cerebral Artery",
                "The largest branch off the Circle of Willis, and the vessel most often involved in an ischaemic stroke.",
                new Vector3(17f, 5.5f, 0f), 10f,
                Combine(mcaW.GetComponentsInChildren<Renderer>(), mcaE.GetComponentsInChildren<Renderer>()));

            CreateAnatomyMarker(root.transform, "cerebral_arteries", "Cerebral Arteries",
                "The vessels carrying blood from the Circle of Willis out across the cortex, where the exchange the brain depends on takes place.",
                new Vector3(0f, 5.5f, 21f), 11f, cerebral.GetComponentsInChildren<Renderer>());

            CreateAnatomyMarker(root.transform, "thrombus", "Thrombus",
                "A clot blocking the vessel. An occlusion here is what causes an ischaemic stroke - the tissue downstream loses its supply.",
                new Vector3(0f, 5f, -20f), 8f, new[] { thrombus.GetComponent<Renderer>() });

            // Only the cerebral corridor ends in open space; every other corridor
            // end butts into a junction disc, which is enclosed by its own wall.
            CreateInvisibleBarrier(root.transform, "Barrier_CerebralEnd", new Vector3(0f, 4f, 29.5f), new Vector3(8f, 8f, 0.5f));

            Transform spawn = CreateAnchor(root.transform, "SpawnPoint", new Vector3(0f, 1.2f, -35f), Quaternion.identity);
            Transform exit = CreateAnchor(root.transform, "ExitAnchor", new Vector3(0f, 1.5f, 26f), Quaternion.identity);

            MarkStatic(root);
            return new LevelEnvironment { Root = root, SpawnPoint = spawn, ExitAnchor = exit };
        }

        /// <summary>
        /// A floor disc enclosed by a ring wall with a gap per attached corridor.
        ///
        /// This is how branches are made. Two crossing corridors would lay their
        /// side walls across each other's route; a disc has no internal walls at
        /// all, so every corridor meeting it stays connected.
        /// </summary>
        private static void BuildJunctionDisc(Transform parent, string name, Vector3 centre, float radius, Vector2[] gaps)
        {
            var junction = new GameObject(name);
            junction.transform.SetParent(parent, false);
            junction.transform.localPosition = centre;

            CreateFloorDisc(junction.transform, "Floor", Vector3.zero, radius * 2f);
            BuildRingWall(junction.transform, radius, 24, 8f, gaps, thickness: 1.6f);
        }


        // ------------------------------------------------------------------
        // Level 3 - Right Ventricle (Phase 8)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the right ventricle and the start of the pulmonary circuit.
        ///
        /// Anatomy represented (blood flows in the correct direction):
        ///   right atrium -> TRICUSPID VALVE (3 cusps) -> right ventricle
        ///   -> PULMONARY VALVE (semilunar) -> pulmonary artery -> lungs
        ///
        /// Deliberately built to contrast with Level 1 rather than mirror it,
        /// because the differences are the teaching content:
        ///  * the wall is visibly thinner (1.4 against Level 1's 2.2) - the RV
        ///    pumps to the lungs at roughly a fifth of the left ventricle's
        ///    pressure, so its myocardium is far thinner;
        ///  * the inflow valve has three cusps, not two;
        ///  * the outflow vessel is rendered in the deoxygenated blue, because
        ///    the pulmonary artery is the one artery carrying deoxygenated
        ///    blood - the most commonly misremembered fact in this topic;
        ///  * the septum sits on the +X side, since the right ventricle is on
        ///    the opposite side of it from the left;
        ///  * the moderator band is present, which the left ventricle has no
        ///    equivalent of.
        /// </summary>
        public static LevelEnvironment BuildRightVentricle()
        {
            var root = new GameObject("Environment_RightVentricle");

            const float chamberRadius = 13f;
            const float wallHeight = 11f;

            GameObject floor = CreateFloorDisc(root.transform, "Floor_Endocardium", Vector3.zero, chamberRadius * 2f);

            // A thinner wall than Level 1's: this is the low-pressure pump.
            BuildRingWall(root.transform, chamberRadius, 34, wallHeight,
                          new[] { new Vector2(0f, 36f), new Vector2(180f, 36f) },
                          thickness: 1.4f);

            // ---- Inflow: the tricuspid valve, three cusps ----
            BuildCorridor(root.transform, "Corridor_RightAtrium", new Vector3(0f, 0f, -19f), new Vector3(7f, 8f, 13f));
            Renderer[] tricuspidFlaps = BuildValveFlaps(root.transform, "Valve_Tricuspid",
                                                        new Vector3(0f, 0f, -13f), 6.5f, leaflets: 3);

            // ---- Outflow: pulmonary valve into the pulmonary artery ----
            GameObject pulmonaryArtery = BuildCorridor(root.transform, "Corridor_PulmonaryArtery",
                                                       new Vector3(0f, 0f, 20f), new Vector3(7f, 8f, 15f),
                                                       wallMaterial: ProjectAssets.Deoxygenated);
            Renderer[] pulmonaryFlaps = BuildValveFlaps(root.transform, "Valve_Pulmonary",
                                                        new Vector3(0f, 0f, 13f), 6.5f, leaflets: 3);

            // ---- Papillary muscles: the right ventricle has three ----
            var papillary = new List<Renderer>();
            papillary.Add(CreateBlock(root.transform, "PapillaryMuscle_Anterior",
                                      new Vector3(-4.8f, 2.2f, -2.5f), new Vector3(1.8f, 4.4f, 1.8f), ProjectAssets.MuscleWall).GetComponent<Renderer>());
            papillary.Add(CreateBlock(root.transform, "PapillaryMuscle_Posterior",
                                      new Vector3(4.8f, 2.2f, -2.5f), new Vector3(1.8f, 4.4f, 1.8f), ProjectAssets.MuscleWall).GetComponent<Renderer>());
            papillary.Add(CreateBlock(root.transform, "PapillaryMuscle_Septal",
                                      new Vector3(7.2f, 2.2f, 3.5f), new Vector3(1.8f, 4.4f, 1.8f), ProjectAssets.MuscleWall).GetComponent<Renderer>());

            // ---- Moderator band: unique to the right ventricle ----
            // Raised above head height so it reads as a crossing band without
            // closing the cavity to the player or to the A* grid.
            GameObject moderatorBand = CreateBlock(root.transform, "ModeratorBand",
                                                   new Vector3(2.5f, 5.4f, 0f), new Vector3(11f, 1.3f, 1.6f), ProjectAssets.MuscleWallDark);

            // ---- Interventricular septum, on the far side from Level 1 ----
            GameObject septum = CreateBlock(root.transform, "InterventricularSeptum",
                                            new Vector3(11.6f, 4f, 0f), new Vector3(2.5f, 8f, 12f), ProjectAssets.MuscleWallDark);

            BuildPlaqueHazard(root.transform, "Hazard_FattyPlaque_Pulmonary", new Vector3(-2.1f, 0f, 19f), new Vector3(4f, 1.2f, 4f), 10);

            // ---- Educational signage ----
            CreateAnatomyMarker(root.transform, "right_ventricle", "Right Ventricle",
                "Pumps deoxygenated blood to the lungs. Its wall is far thinner than the left ventricle's, because the pulmonary circuit needs much less pressure.",
                new Vector3(0f, 6.5f, 0f), 12f, new[] { floor.GetComponent<Renderer>() });

            CreateAnatomyMarker(root.transform, "tricuspid_valve", "Tricuspid Valve",
                "Three cusps between the right atrium and right ventricle. It closes as the ventricle contracts so blood cannot return to the atrium.",
                new Vector3(0f, 5f, -13f), 10f, tricuspidFlaps);

            CreateAnatomyMarker(root.transform, "pulmonary_valve", "Pulmonary Valve",
                "A semilunar valve with three cusps at the outlet. It opens to let blood into the pulmonary artery, then seals so it cannot drain back.",
                new Vector3(0f, 5f, 13f), 10f, pulmonaryFlaps);

            CreateAnatomyMarker(root.transform, "pulmonary_artery", "Pulmonary Artery",
                "The only artery carrying deoxygenated blood. It leaves the right ventricle and divides to both lungs.",
                new Vector3(0f, 5.5f, 22f), 11f, pulmonaryArtery.GetComponentsInChildren<Renderer>());

            CreateAnatomyMarker(root.transform, "moderator_band", "Moderator Band",
                "A muscular band crossing the right ventricle, carrying part of the conduction system to the anterior papillary muscle. The left ventricle has no equivalent.",
                new Vector3(2.5f, 6.4f, 0f), 9f, new[] { moderatorBand.GetComponent<Renderer>() });

            CreateAnatomyMarker(root.transform, "papillary_muscle", "Papillary Muscles",
                "Three muscular pillars anchoring the tricuspid cusps through the chordae tendineae, holding them shut against ventricular pressure.",
                new Vector3(0f, 5.5f, -2.5f), 9f, papillary.ToArray());

            CreateAnatomyMarker(root.transform, "interventricular_septum", "Interventricular Septum",
                "The muscular wall between the ventricles. From this side, the left ventricle lies on the far face.",
                new Vector3(11.6f, 7f, 0f), 9f, new[] { septum.GetComponent<Renderer>() });

            CreateInvisibleBarrier(root.transform, "Barrier_AtriumEnd", new Vector3(0f, 4f, -25.5f), new Vector3(9f, 8f, 0.5f));
            CreateInvisibleBarrier(root.transform, "Barrier_PulmonaryEnd", new Vector3(0f, 4f, 27.5f), new Vector3(9f, 8f, 0.5f));

            Transform spawn = CreateAnchor(root.transform, "SpawnPoint", new Vector3(0f, 1.2f, -20f), Quaternion.identity);
            Transform exit = CreateAnchor(root.transform, "ExitAnchor", new Vector3(0f, 1.5f, 25f), Quaternion.identity);

            MarkStatic(root);
            return new LevelEnvironment { Root = root, SpawnPoint = spawn, ExitAnchor = exit };
        }

        /// <summary>Concatenates two renderer arrays - a structure often owns geometry on both sides.</summary>
        private static Renderer[] Combine(Renderer[] a, Renderer[] b)
        {
            var all = new List<Renderer>(a);
            all.AddRange(b);
            return all.ToArray();
        }

        // ------------------------------------------------------------------
        // Building blocks
        // ------------------------------------------------------------------

        /// <summary>A solid voxel block with a collider. Environment blocks keep their collider.</summary>
        public static GameObject CreateBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;

            int layer = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            if (layer >= 0) block.layer = layer;

            ApplyCheapRendering(block, material);
            return block;
        }

        private static GameObject CreateFloorDisc(Transform parent, string name, Vector3 centre, float diameter)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, false);

            // A Unity cylinder is 1 unit across and 2 units tall by default,
            // so this puts the walking surface exactly at y = 0.
            disc.transform.localPosition = centre + new Vector3(0f, -0.5f, 0f);
            disc.transform.localScale = new Vector3(diameter, 0.5f, diameter);

            // A primitive cylinder ships with a CapsuleCollider. Scaled to a wide
            // flat disc that collider degenerates into a huge sphere (radius is
            // driven by the largest of X/Z), which the player would stand on top
            // of. Replace it with a box matching the mesh bounds; the chamber
            // walls hide the square corners.
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            var box = disc.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(1f, 2f, 1f);

            int layer = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            if (layer >= 0) disc.layer = layer;

            ApplyCheapRendering(disc, ProjectAssets.Endocardium);
            return disc;
        }

        /// <summary>
        /// Places blocks around a circle to form a chamber wall.
        /// </summary>
        /// <param name="gaps">
        /// (centreAngleDegrees, widthDegrees) pairs where no block is placed -
        /// these become the valve openings.
        /// </param>
        private static void BuildRingWall(Transform parent, float radius, int segments, float height, Vector2[] gaps,
                                          float thickness = 2.2f)
        {
            var ring = new GameObject("ChamberWall_Myocardium");
            ring.transform.SetParent(parent, false);

            float step = 360f / segments;
            float blockWidth = 2f * Mathf.PI * radius / segments * 1.25f; // 25% overlap, so no seams

            for (int i = 0; i < segments; i++)
            {
                float angle = i * step;
                if (IsInsideGap(angle, gaps)) continue;

                float rad = angle * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Sin(rad) * radius, height * 0.5f, Mathf.Cos(rad) * radius);

                GameObject block = CreateBlock(ring.transform, $"Wall_{i:00}", position,
                                               new Vector3(blockWidth, height, thickness),
                                               i % 2 == 0 ? ProjectAssets.MuscleWall : ProjectAssets.MuscleWallDark);

                // Face the chamber centre so the wall reads as curved.
                block.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            }
        }

        private static bool IsInsideGap(float angle, Vector2[] gaps)
        {
            if (gaps == null) return false;

            foreach (Vector2 gap in gaps)
            {
                float delta = Mathf.Abs(Mathf.DeltaAngle(angle, gap.x));
                if (delta <= gap.y * 0.5f) return true;
            }
            return false;
        }

        /// <summary>An open-topped tunnel: floor plus two side walls.</summary>
        private static GameObject BuildCorridor(Transform parent, string name, Vector3 centre, Vector3 size,
                                                Material wallMaterial = null)
        {
            var corridor = new GameObject(name);
            corridor.transform.SetParent(parent, false);
            corridor.transform.localPosition = centre;

            CreateBlock(corridor.transform, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(size.x, 1f, size.z), ProjectAssets.Endocardium);
            CreateBlock(corridor.transform, "Wall_L", new Vector3(-size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(1.2f, size.y, size.z), wallMaterial ?? ProjectAssets.MuscleWall);
            CreateBlock(corridor.transform, "Wall_R", new Vector3(size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(1.2f, size.y, size.z), wallMaterial ?? ProjectAssets.MuscleWall);

            return corridor;
        }

        /// <summary>Two valve leaflets framing an opening. Returns their renderers so a hint can highlight them.</summary>
        private static Renderer[] BuildValveFlaps(Transform parent, string name, Vector3 centre, float openingWidth,
                                                  int leaflets = 2)
        {
            var valve = new GameObject(name);
            valve.transform.SetParent(parent, false);
            valve.transform.localPosition = centre;

            var renderers = new List<Renderer>();

            GameObject left = CreateBlock(valve.transform, "Leaflet_L", new Vector3(-openingWidth * 0.5f - 0.6f, 2.5f, 0f), new Vector3(1.6f, 5f, 1.2f), ProjectAssets.ValveTissue);
            GameObject right = CreateBlock(valve.transform, "Leaflet_R", new Vector3(openingWidth * 0.5f + 0.6f, 2.5f, 0f), new Vector3(1.6f, 5f, 1.2f), ProjectAssets.ValveTissue);
            GameObject lintel = CreateBlock(valve.transform, "Annulus", new Vector3(0f, 5.6f, 0f), new Vector3(openingWidth + 3.8f, 1.2f, 1.2f), ProjectAssets.ValveTissue);

            renderers.Add(left.GetComponent<Renderer>());
            renderers.Add(right.GetComponent<Renderer>());
            renderers.Add(lintel.GetComponent<Renderer>());

            // A third cusp, for the tricuspid and the semilunar valves. It hangs
            // from the annulus rather than standing on the floor, so the doorway
            // stays walkable from y=0 to y=3.8.
            //
            // That height is not cosmetic. The A* sampler rejects a cell whose
            // clearance sphere at body height is blocked, so a centre cusp at
            // walking height would seal the chamber off from the corridor - the
            // exact failure the Level 1 valve annulus caused in Phase 5.
            if (leaflets >= 3)
            {
                GameObject centreCusp = CreateBlock(valve.transform, "Leaflet_C", new Vector3(0f, 4.4f, 0f),
                                                    new Vector3(openingWidth * 0.55f, 1.2f, 1.2f), ProjectAssets.ValveTissue);
                renderers.Add(centreCusp.GetComponent<Renderer>());
            }

            return renderers.ToArray();
        }

        /// <summary>Static fatty tissue: a visual mound plus a damaging trigger volume.</summary>
        private static GameObject BuildPlaqueHazard(Transform parent, string name, Vector3 position, Vector3 size, int damage)
        {
            var hazard = new GameObject(name);
            hazard.transform.SetParent(parent, false);
            hazard.transform.localPosition = position;

            // Visual: a small stepped mound, blocking but walkable-around.
            // Placed on the Obstacle layer so the A* grid treats it as a static
            // blockade and routes agents around it (PSM1 section 15), rather
            // than as ordinary scenery.
            GameObject baseBlock = CreateBlock(hazard.transform, "Plaque_Base", new Vector3(0f, size.y * 0.25f, 0f), new Vector3(size.x, size.y * 0.5f, size.z), ProjectAssets.Plaque);
            GameObject topBlock = CreateBlock(hazard.transform, "Plaque_Top", new Vector3(0f, size.y * 0.7f, 0f), new Vector3(size.x * 0.6f, size.y * 0.5f, size.z * 0.6f), ProjectAssets.Plaque);

            int obstacleLayer = LayerMask.NameToLayer(GameConstants.LayerObstacle);
            if (obstacleLayer >= 0)
            {
                baseBlock.layer = obstacleLayer;
                topBlock.layer = obstacleLayer;
            }

            // Damage trigger: slightly larger than the mound so brushing past it counts.
            var trigger = new GameObject("DamageVolume");
            trigger.transform.SetParent(hazard.transform, false);
            trigger.tag = GameConstants.TagHazard;

            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x + 1f, size.y + 2f, size.z + 1f);
            box.center = new Vector3(0f, size.y * 0.5f, 0f);

            var volume = trigger.AddComponent<HazardVolume>();
            using (var w = new EditorWiring(volume))
            {
                w.SetInt("damagePerTick", damage);
            }

            return hazard;
        }

        // ------------------------------------------------------------------
        // Labels and anchors
        // ------------------------------------------------------------------

        /// <summary>A world-space TextMeshPro label.</summary>
        public static TMP_Text CreateWorldLabel(Transform parent, string text, Vector3 position, float fontSize, float width = 12f)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var label = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = ProjectAssets.ResolveFont();
            if (font != null) label.font = font;

            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.95f, 0.85f);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, 4f);

            return label;
        }

        /// <summary>Creates an <see cref="AnatomyMarker"/> with a proximity label.</summary>
        public static AnatomyMarker CreateAnatomyMarker(Transform parent, string structureId, string displayName,
                                                        string description, Vector3 position, float revealRadius,
                                                        Renderer[] highlightRenderers)
        {
            var go = new GameObject($"Anatomy_{structureId}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            TMP_Text label = CreateWorldLabel(go.transform, displayName, Vector3.zero, 2.4f);

            var marker = go.AddComponent<AnatomyMarker>();
            using (var w = new EditorWiring(marker))
            {
                w.SetString("structureId", structureId);
                w.SetString("displayName", displayName);
                w.SetString("description", description);
                w.Set("worldLabel", label);
                w.SetFloat("revealRadius", revealRadius);
                w.SetArray("highlightRenderers", System.Array.ConvertAll(highlightRenderers ?? System.Array.Empty<Renderer>(), r => (Object)r));
            }

            // Tag every renderer this structure owns. A puzzle answer is resolved by
            // raycasting the scene, and the ray hits a block - not the marker, which
            // is only a floating label. Without these tags the geometry is anonymous
            // and no structure puzzle could be answered.
            TagStructureGeometry(structureId, marker, highlightRenderers);

            label.gameObject.SetActive(false);   // revealed by proximity at runtime
            return marker;
        }

        /// <summary>Attaches an <see cref="AnatomyStructureTag"/> to each renderer of a structure.</summary>
        private static void TagStructureGeometry(string structureId, AnatomyMarker marker, Renderer[] renderers)
        {
            if (renderers == null) return;

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;

                var tag = r.gameObject.GetComponent<AnatomyStructureTag>();
                if (tag == null) tag = r.gameObject.AddComponent<AnatomyStructureTag>();

                using (var w = new EditorWiring(tag))
                {
                    w.SetString("structureId", structureId);
                    w.Set("marker", marker);
                }
            }
        }

        /// <summary>
        /// A collision-only wall: BoxCollider, no MeshRenderer, no mesh.
        ///
        /// Placed on the Environment layer so it blocks the A* grid as well as
        /// the player - agents should not walk into the void either. Its top
        /// sits well above <c>maxWalkableHeight</c>, so the grid sampler treats
        /// it as a wall rather than mistaking it for standable floor.
        /// </summary>
        public static GameObject CreateInvisibleBarrier(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            int layer = LayerMask.NameToLayer(GameConstants.LayerEnvironment);
            if (layer >= 0) go.layer = layer;

            var box = go.AddComponent<BoxCollider>();
            box.size = size;

            return go;
        }

        public static Transform CreateAnchor(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            return go.transform;
        }

        // ------------------------------------------------------------------
        // Rendering / lighting
        // ------------------------------------------------------------------

        /// <summary>
        /// Flat shading with no shadow work. The voxel look does not need
        /// shadows, and dropping them is the single biggest saving available
        /// on integrated laptop GPUs.
        /// </summary>
        private static void ApplyCheapRendering(GameObject go, Material material)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Flags geometry as batching-static so Unity can combine draw calls.</summary>
        private static void MarkStatic(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.GetComponent<MeshRenderer>() == null) continue;

                GameObjectUtility.SetStaticEditorFlags(child.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        /// <summary>One directional light, flat ambient and fog. No baked lighting, no skybox.</summary>
        public static void SetupLighting(Color ambient, Color fogColor, float fogDensity)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.90f);
            light.intensity = 1.05f;
            light.shadows = LightShadows.None;   // see ApplyCheapRendering
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }
    }
}
