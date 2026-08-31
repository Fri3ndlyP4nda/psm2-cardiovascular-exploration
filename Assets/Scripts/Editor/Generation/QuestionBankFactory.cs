using System.Collections.Generic;
using Cardio.Core;
using Cardio.Data;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Seeds the three question banks.
    ///
    /// The puzzles are stored as sub-assets of their bank, so each level is one
    /// file in Assets/Data that can still be expanded and edited row by row in
    /// the Inspector.
    ///
    /// Unlike the scene generator, this one does NOT overwrite by default.
    /// Educational content is exactly the kind of thing a supervisor will ask
    /// to be reworded, and silently reverting those edits on the next rebuild
    /// would be worse than useless. Use PSM2 > Content > Reseed Question Banks
    /// (Destructive) to force a rebuild.
    ///
    /// Anatomy note: every TargetStructureId below must exist as an
    /// AnatomyStructureTag in the matching level scene, or the puzzle cannot be
    /// answered. PSM2 > Content > Validate Question Banks checks this.
    /// </summary>
    public static class QuestionBankFactory
    {
        public const string DataFolder = "Assets/Data";

        public static string BankPath(LevelId level) => $"{DataFolder}/QuestionBank_{level}.asset";

        /// <summary>Creates any missing bank. Returns all three, in level order.</summary>
        public static List<QuestionBank> CreateBanks(bool forceReseed)
        {
            if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets", "Data");

            var banks = new List<QuestionBank>
            {
                CreateBank(LevelId.Level1_LeftVentricle, forceReseed, BuildLevel1),
                CreateBank(LevelId.Level2_Brain, forceReseed, BuildLevel2),
                CreateBank(LevelId.Level3_RightVentricle, forceReseed, BuildLevel3)
            };

            AssetDatabase.SaveAssets();
            return banks;
        }

        private static QuestionBank CreateBank(LevelId level, bool forceReseed, System.Func<List<PuzzleData>> builder)
        {
            string path = BankPath(level);
            var existing = AssetDatabase.LoadAssetAtPath<QuestionBank>(path);

            if (existing != null && !forceReseed)
            {
                Debug.Log($"[PSM2] Question bank for {level} already exists with {existing.Count} puzzles - left untouched.");
                return existing;
            }

            if (existing != null) AssetDatabase.DeleteAsset(path);

            var bank = ScriptableObject.CreateInstance<QuestionBank>();
            bank.Level = level;
            bank.Puzzles = new List<PuzzleData>();
            AssetDatabase.CreateAsset(bank, path);

            foreach (PuzzleData puzzle in builder())
            {
                puzzle.name = puzzle.PuzzleId;
                AssetDatabase.AddObjectToAsset(puzzle, bank);
                bank.Puzzles.Add(puzzle);
            }

            EditorUtility.SetDirty(bank);
            Debug.Log($"[PSM2] Seeded question bank for {level} with {bank.Count} puzzles.");
            return bank;
        }

        // ------------------------------------------------------------------
        // Builders
        // ------------------------------------------------------------------

        private static PuzzleData Structure(string id, PuzzleType type, int complexity, string prompt,
                                            string targetStructureId, string explanation, string hint, string label = null)
        {
            var p = ScriptableObject.CreateInstance<PuzzleData>();
            p.PuzzleId = id;
            p.Type = type;
            p.Complexity = complexity;
            p.Prompt = prompt;
            p.TargetStructureId = targetStructureId;
            p.LabelText = label ?? string.Empty;
            p.Explanation = explanation;
            p.Hint = hint;
            return p;
        }

        private static PuzzleData Choice(string id, int complexity, string prompt, string[] options,
                                         int correctIndex, string explanation, string hint)
        {
            var p = ScriptableObject.CreateInstance<PuzzleData>();
            p.PuzzleId = id;
            p.Type = PuzzleType.MultipleChoice;
            p.Complexity = complexity;
            p.Prompt = prompt;
            p.Options = options;
            p.CorrectOptionIndex = correctIndex;
            p.Explanation = explanation;
            p.Hint = hint;
            return p;
        }

        private static PuzzleData Flow(string id, int complexity, string prompt, string[] orderedSteps,
                                       string explanation, string hint)
        {
            var p = ScriptableObject.CreateInstance<PuzzleData>();
            p.PuzzleId = id;
            p.Type = PuzzleType.BloodFlowSequence;
            p.Complexity = complexity;
            p.Prompt = prompt;
            p.SequenceSteps = orderedSteps;
            p.Explanation = explanation;
            p.Hint = hint;
            return p;
        }

        // ------------------------------------------------------------------
        // LEVEL 1 - Left Ventricle (14 puzzles)
        // Structure ids must match EnvironmentFactory.BuildLeftVentricle.
        // ------------------------------------------------------------------

        private static List<PuzzleData> BuildLevel1()
        {
            return new List<PuzzleData>
            {
                Structure("lv1_id_left_ventricle", PuzzleType.IdentifyStructure, 1,
                    "Click the chamber that pumps oxygenated blood into the aorta.",
                    "left_ventricle",
                    "The left ventricle has the thickest myocardium of the four chambers, because it must generate enough pressure to drive blood through the entire systemic circulation.",
                    "It is the large chamber you are standing in."),

                Structure("lv1_id_mitral_valve", PuzzleType.IdentifyStructure, 1,
                    "Click the valve blood passes through when it enters this chamber from the left atrium.",
                    "mitral_valve",
                    "The mitral valve (also called bicuspid, because it has two cusps) sits between the left atrium and the left ventricle. It closes during contraction so blood cannot flow back into the atrium.",
                    "Look back towards the inflow end of the chamber."),

                Structure("lv1_id_aortic_valve", PuzzleType.IdentifyStructure, 1,
                    "Click the valve that opens when the left ventricle contracts.",
                    "aortic_valve",
                    "The aortic valve has three semilunar cusps. It opens during systole to release blood into the aorta, then snaps shut during diastole so blood cannot fall back into the ventricle.",
                    "It is at the outflow end, opposite the mitral valve."),

                Structure("lv1_id_papillary", PuzzleType.IdentifyStructure, 2,
                    "Click the muscular pillars that anchor the mitral valve.",
                    "papillary_muscle",
                    "Papillary muscles contract along with the ventricle and pull on the chordae tendineae. That tension holds the mitral cusps shut so they cannot invert back into the atrium.",
                    "Two thick columns rising from the chamber floor."),

                Structure("lv1_id_septum", PuzzleType.IdentifyStructure, 2,
                    "Click the wall separating this chamber from the right ventricle.",
                    "interventricular_septum",
                    "The interventricular septum is a thick muscular wall. A hole in it is called a ventricular septal defect, which allows oxygenated and deoxygenated blood to mix.",
                    "It is the flat, darker wall along one side of the chamber."),

                Structure("lv1_drag_left_ventricle", PuzzleType.DragAndDropLabel, 1,
                    "Drag the label onto the chamber it names.",
                    "left_ventricle",
                    "Correct. The left ventricle is the systemic pump - the chamber that supplies the whole body except the lungs.",
                    "Drop it on the chamber floor around you.",
                    "Left Ventricle"),

                Structure("lv1_drag_mitral", PuzzleType.DragAndDropLabel, 2,
                    "Drag the label onto the structure it names.",
                    "mitral_valve",
                    "Correct. The mitral valve guards the inflow from the left atrium.",
                    "It is the pale two-leaflet structure at the inflow end.",
                    "Mitral Valve"),

                Structure("lv1_drag_aorta", PuzzleType.DragAndDropLabel, 2,
                    "Drag the label onto the vessel it names.",
                    "aorta",
                    "Correct. The ascending aorta is the body's largest artery, carrying oxygenated blood away from the left ventricle.",
                    "Follow the outflow passage past the aortic valve.",
                    "Aorta"),

                Structure("lv1_valve_backflow", PuzzleType.ValveIdentification, 2,
                    "Click the valve that prevents blood flowing back into the left atrium while the ventricle contracts.",
                    "mitral_valve",
                    "The mitral valve. During systole the rising ventricular pressure forces its two cusps together, sealing the atrium off.",
                    "Backflow into the atrium is stopped at the inflow end."),

                Structure("lv1_valve_semilunar", PuzzleType.ValveIdentification, 3,
                    "Click the semilunar valve of the left heart - the one with three half-moon cusps.",
                    "aortic_valve",
                    "The aortic valve. 'Semilunar' describes the half-moon shape of each of its three cusps. The pulmonary valve on the right side is the other semilunar valve.",
                    "Semilunar valves sit at the exit from a ventricle, not the entrance."),

                Flow("lv1_flow_left_heart", 2,
                    "Put the path of oxygenated blood through the left heart into the correct order.",
                    new[] { "Left Atrium", "Mitral Valve", "Left Ventricle", "Aortic Valve", "Aorta" },
                    "Blood returning from the lungs enters the left atrium, passes the mitral valve into the left ventricle, and is forced through the aortic valve into the aorta.",
                    "Start in the chamber that receives blood from the pulmonary veins."),

                Choice("lv1_mc_thickest_wall", 1,
                    "Which heart chamber has the thickest muscular wall?",
                    new[] { "Left ventricle", "Right ventricle", "Left atrium", "Right atrium" }, 0,
                    "The left ventricle. It pumps against the high resistance of the whole systemic circulation, so it needs far more muscle than the other chambers.",
                    "Think about which chamber has to push blood the furthest."),

                Choice("lv1_mc_blood_type", 1,
                    "What kind of blood does the left ventricle pump?",
                    new[] { "Oxygenated blood", "Deoxygenated blood", "An even mixture of both", "Lymph" }, 0,
                    "Oxygenated. Blood arrives from the lungs via the pulmonary veins into the left atrium, then passes to the left ventricle and out to the body.",
                    "Consider where the blood arriving in the left atrium has just come from."),

                Choice("lv1_mc_chordae", 3,
                    "What connects the papillary muscles to the cusps of the mitral valve?",
                    new[] { "Chordae tendineae", "Coronary arteries", "Purkinje fibres", "The interventricular septum" }, 0,
                    "Chordae tendineae - fine tendinous cords, sometimes called heart strings. They stop the valve cusps from being pushed inside out by ventricular pressure.",
                    "The name translates roughly as 'tendinous cords'.")
            };
        }

        // ------------------------------------------------------------------
        // LEVEL 2 - Brain circulation (15 puzzles)
        // Phase 8 replaced the placeholder room with the real cerebral
        // vessels, so world-picking formats are usable here now.
        // ------------------------------------------------------------------

        private static List<PuzzleData> BuildLevel2()
        {
            return new List<PuzzleData>
            {
                Flow("lv2_flow_carotid", 2,
                    "Order the route blood takes from the heart to the front of the brain.",
                    new[] { "Aortic Arch", "Common Carotid Artery", "Internal Carotid Artery", "Circle of Willis", "Cerebral Arteries" },
                    "The common carotid arises from the aortic arch and divides into external and internal branches. The internal carotid enters the skull and feeds the Circle of Willis.",
                    "Start at the vessel leaving the heart."),

                Flow("lv2_flow_vertebral", 3,
                    "Order the vertebral route to the back of the brain.",
                    new[] { "Subclavian Artery", "Vertebral Artery", "Basilar Artery", "Posterior Cerebral Artery" },
                    "The vertebral arteries branch from the subclavian arteries, run up through the vertebrae, and merge to form the basilar artery, which supplies the posterior brain.",
                    "The two vertebral arteries join before they branch again."),

                Flow("lv2_flow_venous_return", 2,
                    "Order the route deoxygenated blood takes leaving the brain.",
                    new[] { "Cerebral Veins", "Dural Venous Sinuses", "Internal Jugular Vein", "Superior Vena Cava", "Right Atrium" },
                    "Cerebral veins drain into venous sinuses within the dura, which empty into the internal jugular veins and return to the right atrium via the superior vena cava.",
                    "Finish at the chamber that receives all deoxygenated blood from the upper body."),

                Choice("lv2_mc_circle_of_willis", 1,
                    "Which structure is the arterial ring at the base of the brain?",
                    new[] { "Circle of Willis", "Coronary sinus", "Hepatic portal system", "Pulmonary trunk" }, 0,
                    "The Circle of Willis. It links the carotid and vertebrobasilar supplies into a ring, so blood can still reach most of the brain if one feeding artery is narrowed.",
                    "It is named after the physician who described it."),

                Choice("lv2_mc_two_supplies", 1,
                    "Which pair of arterial systems supplies the brain?",
                    new[] { "Internal carotid and vertebral arteries", "Pulmonary and bronchial arteries", "Renal and splenic arteries", "Femoral and iliac arteries" }, 0,
                    "The internal carotid arteries supply the front of the brain and the vertebral arteries the back. They meet at the Circle of Willis.",
                    "One pair runs up the neck at the front, the other through the vertebrae."),

                Choice("lv2_mc_basilar", 2,
                    "The two vertebral arteries merge to form which vessel?",
                    new[] { "Basilar artery", "Aorta", "Superior vena cava", "Carotid sinus" }, 0,
                    "The basilar artery. It runs along the front of the brainstem and supplies the cerebellum and posterior cerebrum.",
                    "It sits at the base of the brain, which is where its name comes from."),

                Choice("lv2_mc_blood_brain_barrier", 2,
                    "What does the blood-brain barrier do?",
                    new[]
                    {
                        "Restricts which substances can pass from blood into brain tissue",
                        "Pumps blood upwards against gravity",
                        "Stores oxygen for the brain",
                        "Filters blood the way the kidney does"
                    }, 0,
                    "It is formed by tight junctions between capillary endothelial cells, letting oxygen and glucose through while blocking many toxins, pathogens and drugs.",
                    "The clue is in the word 'barrier'."),

                Choice("lv2_mc_venous_drainage", 2,
                    "Which vessels return most deoxygenated blood from the brain to the heart?",
                    new[] { "Internal jugular veins", "Pulmonary veins", "Hepatic veins", "Renal veins" }, 0,
                    "The internal jugular veins. They collect from the dural venous sinuses and drain into the brachiocephalic veins and then the superior vena cava.",
                    "They run down the neck alongside the carotid arteries."),

                Choice("lv2_mc_grey_matter", 2,
                    "Which brain tissue has the higher blood flow requirement per gram?",
                    new[] { "Grey matter", "White matter", "The dura mater", "The skull" }, 0,
                    "Grey matter. It is dense with neuron cell bodies and synapses, which are metabolically expensive, so it receives several times the blood flow of white matter.",
                    "Think about which tissue contains the neuron cell bodies."),

                Choice("lv2_mc_oxygen_share", 3,
                    "Roughly what proportion of the body's oxygen does the resting brain consume?",
                    new[] { "About 20%", "About 2%", "About 50%", "About 75%" }, 0,
                    "About 20%, despite the brain being only around 2% of body weight. That mismatch is why even a brief interruption of cerebral blood flow causes symptoms so quickly.",
                    "It is far out of proportion to the brain's share of body weight."),

                Choice("lv2_mc_ischaemic_stroke", 3,
                    "An ischaemic stroke occurs when:",
                    new[]
                    {
                        "An artery supplying part of the brain becomes blocked",
                        "A heart valve begins to leak",
                        "The blood-brain barrier thickens",
                        "Venous sinuses widen"
                    }, 0,
                    "A blockage, usually a clot, cuts off blood flow to a region of brain tissue. Without oxygen and glucose those neurons begin to die within minutes.",
                    "'Ischaemia' means an inadequate blood supply."),

                Choice("lv2_mc_collateral", 3,
                    "Why is the Circle of Willis clinically important?",
                    new[]
                    {
                        "It can reroute blood if one supplying artery becomes blocked",
                        "It stores oxygenated blood for emergencies",
                        "It filters clots out of the blood",
                        "It generates the heartbeat"
                    }, 0,
                    "Because it is a ring rather than a dead end, flow can arrive at a region from more than one direction. This collateral circulation can limit the damage from a single blocked vessel.",
                    "Consider the advantage of a loop over a one-way pipe."),

                // ---- World-picking puzzles (Phase 8, once the real vessels existed) ----
                // Note there is no ValveIdentification puzzle in this bank: the
                // cerebral arteries have no valves, so asking the player to click
                // one would be teaching something false.
                Structure("lv2_id_circle_of_willis", PuzzleType.IdentifyStructure, 1,
                    "Click the ring of arteries that joins the front and back blood supplies of the brain.",
                    "circle_of_willis",
                    "The Circle of Willis. Because it is a closed ring rather than a dead end, blood can reach a region from more than one direction - which is what makes collateral flow possible.",
                    "It is the open circular chamber at the centre of the level."),

                Structure("lv2_id_basilar_artery", PuzzleType.IdentifyStructure, 2,
                    "Click the vessel formed where the two vertebral arteries merge.",
                    "basilar_artery",
                    "The basilar artery. It runs up the front of the brainstem, supplies the brainstem and cerebellum, and feeds the posterior part of the Circle of Willis.",
                    "It is the single wide vessel you started the level in."),

                Structure("lv2_drag_internal_carotid", PuzzleType.DragAndDropLabel, 2,
                    "Drag the label onto the paired vessels that carry the anterior blood supply into the skull.",
                    "internal_carotid",
                    "Correct. The internal carotid arteries are the anterior supply, and together they deliver most of the blood the brain receives.",
                    "They are the two long vessels running down either side of the level.",
                    "Internal Carotid")
            };
        }

        // ------------------------------------------------------------------
        // LEVEL 3 - Right ventricle and pulmonary circulation (15 puzzles)
        // ------------------------------------------------------------------

        private static List<PuzzleData> BuildLevel3()
        {
            return new List<PuzzleData>
            {
                Flow("lv3_flow_right_heart", 2,
                    "Order the path of deoxygenated blood through the right heart.",
                    new[] { "Superior Vena Cava", "Right Atrium", "Tricuspid Valve", "Right Ventricle", "Pulmonary Valve", "Pulmonary Artery" },
                    "Deoxygenated blood returns via the vena cavae to the right atrium, passes the tricuspid valve into the right ventricle, and is pumped through the pulmonary valve into the pulmonary artery.",
                    "Start with the vessel bringing blood down from the head and arms."),

                Flow("lv3_flow_pulmonary_circuit", 3,
                    "Order the pulmonary circuit, from leaving the right ventricle to arriving in the left heart.",
                    new[] { "Pulmonary Artery", "Lungs", "Pulmonary Veins", "Left Atrium" },
                    "This is the pulmonary circulation: blood is sent to the lungs to be oxygenated and returns to the left atrium, ready to be pumped around the body.",
                    "Gas exchange happens in the middle of this sequence."),

                Choice("lv3_mc_tricuspid", 1,
                    "Which valve sits between the right atrium and the right ventricle?",
                    new[] { "Tricuspid valve", "Mitral valve", "Aortic valve", "Pulmonary valve" }, 0,
                    "The tricuspid valve. It is the right-side counterpart of the mitral valve, but has three cusps rather than two.",
                    "Its name tells you how many cusps it has."),

                Choice("lv3_mc_rv_blood", 1,
                    "What kind of blood does the right ventricle pump?",
                    new[] { "Deoxygenated blood", "Oxygenated blood", "An even mixture of both", "Plasma only" }, 0,
                    "Deoxygenated. The right heart receives blood returning from the body and sends it to the lungs to pick up oxygen.",
                    "Consider where the right ventricle sends blood next."),

                Choice("lv3_mc_vena_cava", 1,
                    "Which vessels return deoxygenated blood to the right atrium?",
                    new[] { "Superior and inferior vena cava", "The pulmonary veins", "The aorta and carotids", "The hepatic arteries" }, 0,
                    "The superior vena cava drains the head, neck and arms; the inferior vena cava drains everything below the diaphragm. Both empty into the right atrium.",
                    "One drains the upper body, the other the lower."),

                Choice("lv3_mc_cusps", 2,
                    "How many cusps does the tricuspid valve have?",
                    new[] { "Three", "Two", "Four", "One" }, 0,
                    "Three. The mitral valve on the left side has two, which is why it is also called the bicuspid valve.",
                    "Compare the name with the mitral valve's alternative name."),

                Choice("lv3_mc_pulmonary_artery", 2,
                    "The pulmonary artery is unusual among arteries because:",
                    new[]
                    {
                        "It carries deoxygenated blood away from the heart",
                        "It carries oxygenated blood towards the heart",
                        "It has no muscular wall",
                        "It contains no valves anywhere in the circuit"
                    }, 0,
                    "Arteries are defined by carrying blood away from the heart, not by oxygen content. The pulmonary artery is the main exception to the usual 'arteries carry oxygenated blood' shorthand.",
                    "The definition of an artery is about direction, not oxygen."),

                Choice("lv3_mc_pulmonary_valve", 2,
                    "Which valve stops blood falling back from the pulmonary artery into the right ventricle?",
                    new[] { "Pulmonary valve", "Tricuspid valve", "Mitral valve", "Aortic valve" }, 0,
                    "The pulmonary valve. Like the aortic valve it is semilunar, with three cusps, and closes as the ventricle relaxes.",
                    "It is named after the vessel it guards."),

                Choice("lv3_mc_wall_thickness", 2,
                    "Why is the right ventricle wall thinner than the left ventricle wall?",
                    new[]
                    {
                        "It pumps against the much lower resistance of the lungs",
                        "It holds far less blood",
                        "It beats only half as often",
                        "It contains no cardiac muscle"
                    }, 0,
                    "Both ventricles eject a similar volume per beat, but pulmonary circulation is a low-pressure circuit, so the right ventricle needs far less muscle to do its job.",
                    "Think about pressure rather than volume."),

                Choice("lv3_mc_gas_exchange", 3,
                    "Where in the pulmonary circuit does gas exchange actually take place?",
                    new[]
                    {
                        "In the capillaries surrounding the alveoli",
                        "Inside the right ventricle",
                        "In the wall of the aorta",
                        "In the superior vena cava"
                    }, 0,
                    "In the dense capillary network wrapping each alveolus. The barrier there is thin enough for oxygen and carbon dioxide to diffuse across in a fraction of a second.",
                    "It happens where the blood vessels are thinnest and closest to air."),

                Choice("lv3_mc_pressure", 3,
                    "Compared with the systemic circulation, the pulmonary circulation is:",
                    new[] { "A lower-pressure circuit", "A higher-pressure circuit", "At identical pressure", "Not a closed circuit at all" }, 0,
                    "Much lower pressure. Pulmonary arterial pressure is roughly a fifth of systemic arterial pressure, which protects the delicate alveolar capillaries from damage.",
                    "Delicate lung tissue could not survive systemic pressures."),

                Choice("lv3_mc_circuit_definition", 3,
                    "Pulmonary circulation is best described as the circuit that carries blood:",
                    new[]
                    {
                        "From the right heart to the lungs and back to the left heart",
                        "From the left heart to the body and back",
                        "From the heart to the liver and back",
                        "Between the two atria directly"
                    }, 0,
                    "That is the definition. Systemic circulation is the other loop: left heart, out to the body, back to the right heart. The two run in series.",
                    "It is the loop that involves the lungs."),

                // ---- World-picking puzzles (Phase 8, once the real chamber existed) ----
                Structure("lv3_id_right_ventricle", PuzzleType.IdentifyStructure, 1,
                    "Click the chamber that pumps deoxygenated blood towards the lungs.",
                    "right_ventricle",
                    "The right ventricle. Its wall is markedly thinner than the left ventricle's, because the pulmonary circuit it pumps into runs at roughly a fifth of systemic pressure.",
                    "It is the chamber you are standing in."),

                Structure("lv3_valve_backflow_atrium", PuzzleType.ValveIdentification, 2,
                    "Click the valve that stops blood returning to the right atrium while this chamber contracts.",
                    "tricuspid_valve",
                    "The tricuspid valve. Its three cusps are forced together by rising ventricular pressure, and the papillary muscles hold them from inverting.",
                    "Look back towards the inflow end, where you entered."),

                Structure("lv3_drag_pulmonary_artery", PuzzleType.DragAndDropLabel, 2,
                    "Drag the label onto the vessel that carries blood from this chamber to the lungs.",
                    "pulmonary_artery",
                    "Correct. The pulmonary artery is the only artery carrying deoxygenated blood - arteries are defined by flowing away from the heart, not by oxygen content.",
                    "Follow the outflow past the pulmonary valve; it is rendered in blue.",
                    "Pulmonary Artery")
            };
        }
    }
}
