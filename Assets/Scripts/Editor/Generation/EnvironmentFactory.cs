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

        /// <summary>
        /// A small greybox room with a sign explaining that the real level is
        /// scheduled for Phase 8. It exists so that scene flow, build settings
        /// and level unlocking can be tested end to end from Phase 1 without
        /// pretending the level is finished.
        /// </summary>
        public static LevelEnvironment BuildPlaceholderRoom(string name, string headline, string body, Material wallMaterial)
        {
            var root = new GameObject($"Environment_{name}");

            GameObject floor = CreateBlock(root.transform, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), ProjectAssets.Endocardium);
            floor.GetComponent<Renderer>().sharedMaterial = ProjectAssets.Endocardium;

            // Four perimeter walls.
            CreateBlock(root.transform, "Wall_N", new Vector3(0f, 4f, 20f), new Vector3(40f, 8f, 1.5f), wallMaterial);
            CreateBlock(root.transform, "Wall_S", new Vector3(0f, 4f, -20f), new Vector3(40f, 8f, 1.5f), wallMaterial);
            CreateBlock(root.transform, "Wall_E", new Vector3(20f, 4f, 0f), new Vector3(1.5f, 8f, 40f), wallMaterial);
            CreateBlock(root.transform, "Wall_W", new Vector3(-20f, 4f, 0f), new Vector3(1.5f, 8f, 40f), wallMaterial);

            // A couple of blocks so movement and the camera collision can be tested.
            CreateBlock(root.transform, "Block_A", new Vector3(-6f, 1.5f, 4f), new Vector3(4f, 3f, 4f), wallMaterial);
            CreateBlock(root.transform, "Block_B", new Vector3(7f, 1.5f, -5f), new Vector3(4f, 3f, 4f), wallMaterial);

            BuildPlaqueHazard(root.transform, "Hazard_FattyPlaque", new Vector3(0f, 0f, 8f), new Vector3(5f, 1.2f, 5f), 10);

            CreateWorldLabel(root.transform, $"{headline}\n<size=55%>{body}</size>", new Vector3(0f, 5f, 12f), 3.2f, 18f);

            Transform spawn = CreateAnchor(root.transform, "SpawnPoint", new Vector3(0f, 1.2f, -14f), Quaternion.identity);
            Transform exit = CreateAnchor(root.transform, "ExitAnchor", new Vector3(0f, 1.5f, 16f), Quaternion.identity);

            MarkStatic(root);
            return new LevelEnvironment { Root = root, SpawnPoint = spawn, ExitAnchor = exit };
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
        private static void BuildRingWall(Transform parent, float radius, int segments, float height, Vector2[] gaps)
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
                                               new Vector3(blockWidth, height, 2.2f),
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
        private static GameObject BuildCorridor(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var corridor = new GameObject(name);
            corridor.transform.SetParent(parent, false);
            corridor.transform.localPosition = centre;

            CreateBlock(corridor.transform, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(size.x, 1f, size.z), ProjectAssets.Endocardium);
            CreateBlock(corridor.transform, "Wall_L", new Vector3(-size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(1.2f, size.y, size.z), ProjectAssets.MuscleWall);
            CreateBlock(corridor.transform, "Wall_R", new Vector3(size.x * 0.5f, size.y * 0.5f, 0f), new Vector3(1.2f, size.y, size.z), ProjectAssets.MuscleWall);

            return corridor;
        }

        /// <summary>Two valve leaflets framing an opening. Returns their renderers so a hint can highlight them.</summary>
        private static Renderer[] BuildValveFlaps(Transform parent, string name, Vector3 centre, float openingWidth)
        {
            var valve = new GameObject(name);
            valve.transform.SetParent(parent, false);
            valve.transform.localPosition = centre;

            GameObject left = CreateBlock(valve.transform, "Leaflet_L", new Vector3(-openingWidth * 0.5f - 0.6f, 2.5f, 0f), new Vector3(1.6f, 5f, 1.2f), ProjectAssets.ValveTissue);
            GameObject right = CreateBlock(valve.transform, "Leaflet_R", new Vector3(openingWidth * 0.5f + 0.6f, 2.5f, 0f), new Vector3(1.6f, 5f, 1.2f), ProjectAssets.ValveTissue);
            GameObject lintel = CreateBlock(valve.transform, "Annulus", new Vector3(0f, 5.6f, 0f), new Vector3(openingWidth + 3.8f, 1.2f, 1.2f), ProjectAssets.ValveTissue);

            return new[]
            {
                left.GetComponent<Renderer>(),
                right.GetComponent<Renderer>(),
                lintel.GetComponent<Renderer>()
            };
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
