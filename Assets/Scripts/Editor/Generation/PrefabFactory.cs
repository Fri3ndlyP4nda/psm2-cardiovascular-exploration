using Cardio.AI;
using Cardio.Core;
using Cardio.Player;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Builds the player prefab from Unity primitives.
    ///
    /// The PSM1 art direction is MagicaVoxel-style voxel art. Until those .vox
    /// models are produced and exported, "Bloo.D. Clot" is assembled from
    /// scaled cubes, which matches the intended blocky look, costs almost
    /// nothing to render, and can be swapped for an imported mesh later by
    /// replacing only the Visual child.
    /// </summary>
    public static class PrefabFactory
    {
        public const string PlayerPrefabPath = "Assets/Prefabs/Player/Player_BlooDClot.prefab";

        /// <summary>Creates or rebuilds the player prefab and returns it.</summary>
        public static GameObject CreatePlayerPrefab()
        {
            GameObject root = BuildPlayerObject();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            return prefab;
        }

        private static GameObject BuildPlayerObject()
        {
            var root = new GameObject("Player_BlooDClot");
            root.tag = GameConstants.TagPlayer;

            int playerLayer = LayerMask.NameToLayer(GameConstants.LayerPlayer);
            if (playerLayer >= 0) root.layer = playerLayer;

            // ---- Collision capsule ----
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.6f;
            controller.radius = 0.55f;
            controller.center = new Vector3(0f, 0.8f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;
            controller.skinWidth = 0.06f;

            // ---- Visual: a friendly voxel red blood cell ----
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            // Body: a squashed cube reads as the biconcave disc of an RBC.
            CreateBlock(visual.transform, "Body", Vector3.zero, new Vector3(1.25f, 0.85f, 1.25f), ProjectAssets.BloodCell);

            // Rim blocks give the silhouette its stepped, voxel edge.
            CreateBlock(visual.transform, "Rim_F", new Vector3(0f, 0f, 0.72f), new Vector3(0.75f, 0.55f, 0.3f), ProjectAssets.CellHighlight);
            CreateBlock(visual.transform, "Rim_B", new Vector3(0f, 0f, -0.72f), new Vector3(0.75f, 0.55f, 0.3f), ProjectAssets.CellHighlight);
            CreateBlock(visual.transform, "Rim_L", new Vector3(-0.72f, 0f, 0f), new Vector3(0.3f, 0.55f, 0.75f), ProjectAssets.CellHighlight);
            CreateBlock(visual.transform, "Rim_R", new Vector3(0.72f, 0f, 0f), new Vector3(0.3f, 0.55f, 0.75f), ProjectAssets.CellHighlight);

            // Dimple: the dark centre of a red blood cell.
            CreateBlock(visual.transform, "Dimple", new Vector3(0f, 0.4f, 0f), new Vector3(0.6f, 0.12f, 0.6f), ProjectAssets.CellHighlight);

            // Face, so the character reads as a friendly protagonist rather than a prop.
            CreateBlock(visual.transform, "Eye_L", new Vector3(-0.26f, 0.12f, 0.6f), new Vector3(0.22f, 0.22f, 0.12f), ProjectAssets.EyeWhite);
            CreateBlock(visual.transform, "Eye_R", new Vector3(0.26f, 0.12f, 0.6f), new Vector3(0.22f, 0.22f, 0.12f), ProjectAssets.EyeWhite);
            CreateBlock(visual.transform, "Pupil_L", new Vector3(-0.26f, 0.10f, 0.66f), new Vector3(0.10f, 0.10f, 0.08f), ProjectAssets.EyePupil);
            CreateBlock(visual.transform, "Pupil_R", new Vector3(0.26f, 0.10f, 0.66f), new Vector3(0.10f, 0.10f, 0.08f), ProjectAssets.EyePupil);

            // ---- Behaviour ----
            root.AddComponent<PlayerHealth>();
            root.AddComponent<PlayerInteraction>();   // Phase 2: puzzle station activation
            root.AddComponent<PlayerAttack>();        // oxygen burst vs leukemic blasts
            var playerController = root.AddComponent<PlayerController>();

            using (var w = new EditorWiring(playerController))
            {
                w.Set("visual", visual.transform);
            }

            return root;
        }

        public const string NeutrophilPrefabPath = "Assets/Prefabs/Environment/Obstacle_Neutrophil.prefab";
        public const string MonocytePrefabPath = "Assets/Prefabs/Environment/Obstacle_Monocyte.prefab";

        /// <summary>
        /// Builds the two mobile obstacles from PSM1 section 15.
        ///
        /// Both use the same components and differ only in their serialized
        /// values - small/fast/hunting versus large/slow/obstructing - which is
        /// exactly the distinction PSM1 draws between them.
        /// </summary>
        public static (GameObject neutrophil, GameObject monocyte) CreateObstaclePrefabs()
        {
            GameObject neutrophil = BuildNeutrophil();
            GameObject neutrophilAsset = PrefabUtility.SaveAsPrefabAsset(neutrophil, NeutrophilPrefabPath);
            Object.DestroyImmediate(neutrophil);

            GameObject monocyte = BuildMonocyte();
            GameObject monocyteAsset = PrefabUtility.SaveAsPrefabAsset(monocyte, MonocytePrefabPath);
            Object.DestroyImmediate(monocyte);

            return (neutrophilAsset, monocyteAsset);
        }

        /// <summary>Small, fast, aggressive. Hunts the player through the chamber.</summary>
        private static GameObject BuildNeutrophil()
        {
            GameObject root = BuildAgentShell("Obstacle_Neutrophil", radius: 0.55f, height: 1.3f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.65f, 0f);

            CreateBlock(visual.transform, "Body", Vector3.zero, new Vector3(1.05f, 1.05f, 1.05f), ProjectAssets.Neutrophil);
            CreateBlock(visual.transform, "Lobe_L", new Vector3(-0.3f, 0.12f, 0.1f), new Vector3(0.42f, 0.42f, 0.42f), ProjectAssets.NeutrophilNucleus);
            CreateBlock(visual.transform, "Lobe_R", new Vector3(0.3f, -0.05f, -0.1f), new Vector3(0.38f, 0.38f, 0.38f), ProjectAssets.NeutrophilNucleus);
            CreateBlock(visual.transform, "Lobe_C", new Vector3(0f, -0.2f, 0.22f), new Vector3(0.34f, 0.34f, 0.34f), ProjectAssets.NeutrophilNucleus);

            // Spikes read as "aggressive" at a glance without any animation.
            CreateBlock(visual.transform, "Spike_F", new Vector3(0f, 0f, 0.62f), new Vector3(0.22f, 0.22f, 0.3f), ProjectAssets.NeutrophilNucleus);
            CreateBlock(visual.transform, "Spike_B", new Vector3(0f, 0f, -0.62f), new Vector3(0.22f, 0.22f, 0.3f), ProjectAssets.NeutrophilNucleus);

            var pathfinder = root.AddComponent<PathfindingAgent>();
            using (var w = new EditorWiring(pathfinder))
            {
                w.SetFloat("baseSpeed", 2.6f);          // the player moves at 5, so they can be outrun
                w.SetFloat("stoppingDistance", 0.9f);
                w.SetFloat("repathInterval", 0.5f);
            }

            var obstacle = root.AddComponent<ObstacleAgent>();
            using (var w = new EditorWiring(obstacle))
            {
                w.SetInt("kind", (int)ObstacleKind.Neutrophil);
                w.SetInt("behaviour", (int)ObstacleBehaviour.Chase);
                w.SetFloat("detectionRadius", 16f);
                w.SetFloat("loseInterestRadius", 24f);
                w.SetInt("contactDamage", 10);
                w.SetFloat("contactRadius", 1.1f);
            }

            return root;
        }

        /// <summary>Large, slow, bulky. Patrols and gets in the way.</summary>
        private static GameObject BuildMonocyte()
        {
            GameObject root = BuildAgentShell("Obstacle_Monocyte", radius: 0.95f, height: 2f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);

            CreateBlock(visual.transform, "Body", Vector3.zero, new Vector3(1.85f, 1.7f, 1.85f), ProjectAssets.Monocyte);
            CreateBlock(visual.transform, "Nucleus", new Vector3(0f, 0.15f, 0.15f), new Vector3(1.0f, 0.85f, 0.9f), ProjectAssets.MonocyteNucleus);

            // Rim blocks make the silhouette read as bigger than the neutrophil.
            CreateBlock(visual.transform, "Bulge_L", new Vector3(-0.9f, -0.1f, 0f), new Vector3(0.5f, 0.9f, 1.1f), ProjectAssets.Monocyte);
            CreateBlock(visual.transform, "Bulge_R", new Vector3(0.9f, -0.1f, 0f), new Vector3(0.5f, 0.9f, 1.1f), ProjectAssets.Monocyte);

            var pathfinder = root.AddComponent<PathfindingAgent>();
            using (var w = new EditorWiring(pathfinder))
            {
                w.SetFloat("baseSpeed", 1.3f);
                w.SetFloat("stoppingDistance", 1.6f);
                w.SetFloat("repathInterval", 1.2f);
                w.SetFloat("waypointTolerance", 1.1f);   // bulky bodies need a looser corner
            }

            var obstacle = root.AddComponent<ObstacleAgent>();
            using (var w = new EditorWiring(obstacle))
            {
                w.SetInt("kind", (int)ObstacleKind.Monocyte);
                w.SetInt("behaviour", (int)ObstacleBehaviour.Patrol);
                w.SetInt("contactDamage", 15);
                w.SetFloat("contactRadius", 1.7f);
                w.SetFloat("patrolWaitSeconds", 1.5f);
            }

            return root;
        }

        public const string BlastPrefabPath = "Assets/Prefabs/Environment/Hostile_LeukemicBlast.prefab";

        /// <summary>
        /// Builds the leukemic blast cell - the only hostile the player fights.
        ///
        /// Visually distinct from the healthy immune cells in three ways at once,
        /// so it is unmistakable at a glance: violet instead of cream or blue,
        /// an oversized nucleus filling most of the body instead of small lobes,
        /// and irregular asymmetric blebs instead of a symmetric silhouette.
        /// </summary>
        public static GameObject CreateBlastPrefab()
        {
            GameObject root = BuildAgentShell("Hostile_LeukemicBlast", radius: 0.7f, height: 1.6f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            // Thin rim of cytoplasm...
            GameObject body = CreateBlock(visual.transform, "Cytoplasm", Vector3.zero, new Vector3(1.35f, 1.3f, 1.35f), ProjectAssets.BlastBody);

            // ...around a nucleus that takes up most of the cell. That ratio is
            // the textbook marker of a blast, and it reads instantly as "wrong"
            // next to a neutrophil's small multi-lobed nucleus.
            GameObject nucleus = CreateBlock(visual.transform, "Nucleus", new Vector3(0f, 0.05f, 0f), new Vector3(1.0f, 0.98f, 1.0f), ProjectAssets.BlastNucleus);

            // Irregular blebs - deliberately asymmetric, unlike the neutrophil's
            // matched spikes or the monocyte's even bulges.
            CreateBlock(visual.transform, "Bleb_A", new Vector3(0.62f, 0.24f, 0.18f), new Vector3(0.4f, 0.34f, 0.3f), ProjectAssets.BlastBody);
            CreateBlock(visual.transform, "Bleb_B", new Vector3(-0.58f, -0.18f, 0.3f), new Vector3(0.34f, 0.42f, 0.28f), ProjectAssets.BlastBody);
            CreateBlock(visual.transform, "Bleb_C", new Vector3(0.1f, -0.3f, -0.6f), new Vector3(0.46f, 0.28f, 0.3f), ProjectAssets.BlastBody);
            CreateBlock(visual.transform, "Bleb_D", new Vector3(-0.2f, 0.55f, -0.35f), new Vector3(0.3f, 0.3f, 0.36f), ProjectAssets.BlastBody);

            var pathfinder = root.AddComponent<PathfindingAgent>();
            using (var w = new EditorWiring(pathfinder))
            {
                w.SetFloat("baseSpeed", 3f);          // faster than a neutrophil, still outrunnable at player speed 5
                w.SetFloat("stoppingDistance", 1f);
                w.SetFloat("repathInterval", 0.45f);
            }

            var obstacle = root.AddComponent<ObstacleAgent>();
            using (var w = new EditorWiring(obstacle))
            {
                w.SetInt("kind", (int)ObstacleKind.Neutrophil);   // shape of behaviour only; identity comes from LeukemicBlastAgent
                w.SetInt("behaviour", (int)ObstacleBehaviour.Chase);
                w.SetFloat("detectionRadius", 20f);
                w.SetFloat("loseInterestRadius", 30f);
                w.SetInt("contactDamage", 12);
                w.SetFloat("contactRadius", 1.2f);
            }

            var health = root.AddComponent<NpcHealth>();
            using (var w = new EditorWiring(health))
            {
                w.SetInt("maxHealth", 100);   // three swings at 34 damage
            }

            var blast = root.AddComponent<LeukemicBlastAgent>();
            using (var w = new EditorWiring(blast))
            {
                w.SetArray("bodyRenderers", new Object[]
                {
                    body.GetComponent<Renderer>(),
                    nucleus.GetComponent<Renderer>()
                });
            }

            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, BlastPrefabPath);
            Object.DestroyImmediate(root);

            return asset;
        }

        /// <summary>Root object with the CharacterController every agent moves through.</summary>
        private static GameObject BuildAgentShell(string name, float radius, float height)
        {
            var root = new GameObject(name);

            var controller = root.AddComponent<CharacterController>();
            controller.radius = radius;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.slopeLimit = 55f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.05f;

            return root;
        }

        /// <summary>
        /// Creates one voxel block. Colliders are stripped: the CharacterController
        /// is the only collider the player needs, and extra colliders on the visual
        /// would fight with it.
        /// </summary>
        public static GameObject CreateBlock(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;

            Object.DestroyImmediate(block.GetComponent<Collider>());

            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return block;
        }
    }
}
