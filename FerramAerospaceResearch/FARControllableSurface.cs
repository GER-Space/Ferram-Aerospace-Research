﻿/*
Ferram Aerospace Research v0.13.1
Copyright 2013, Michael Ferrara, aka Ferram4

    This file is part of Ferram Aerospace Research.

    Ferram Aerospace Research is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Kerbal Joint Reinforcement is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Ferram Aerospace Research.  If not, see <http://www.gnu.org/licenses/>.

 * 
 * Kerbal Engineer Redux created by Cybutek, Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License
 *      Referenced for starting point for fixing the "editor click-through-GUI" bug
 *
 * Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/55219
 *
 * Toolbar integration powered by blizzy78's Toolbar plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/60863
 */



using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using KSP;

namespace ferram4
{
    public class FARControllableSurface : FARWingAerodynamicModel
    {
        
        
        protected Transform movableSection = null;

        protected Transform MovableSection
        {
            get
            {
                if (movableSection == null)
                {
                    movableSection = part.FindModelTransform("obj_ctrlSrf");     //And the transform
                    if (!MovableOrigReady)
                    {
                        // In parts copied by symmetry, these fields should already be set,
                        // while the transform may not be in the original orientation anymore.
                        MovableOrig = movableSection.localRotation;         //Its original orientation
                        MovableOrigReady = true;
                    }
                    if (Vector3.Dot(MovableSection.right, part.transform.right) > 0)
                        flipAxis = false;
                    else
                        flipAxis = true;
                }
                return movableSection;
            }
        }

        private bool flipAxis = false;


        [KSPField(isPersistant = false)]
        public Vector3 controlSurfacePivot = new Vector3(1f, 0f, 0f);

        [KSPField(isPersistant = false)]
        public float ctrlSurfFrac = 1;

        // These TWO fields MUST be set up so that they are copied by Object.Instantiate.
        // Otherwise detaching and re-attaching wings with deflected flaps etc breaks until save/load.
        [SerializeField]
        protected Quaternion MovableOrig = Quaternion.identity;
        [SerializeField]
        private bool MovableOrigReady = false;

//        protected int MovableSectionFlip = 1;

        [UI_Toggle(enabledText = "Active", scene = UI_Scene.Editor, disabledText = "Inactive")]
        [KSPField(guiName = "Pitch", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        public bool pitchaxis = true;

        [UI_Toggle(enabledText = "Active", scene = UI_Scene.Editor, disabledText = "Inactive")]
        [KSPField(guiName = "Yaw", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        public bool yawaxis = true;

        [UI_Toggle(enabledText = "Active", scene = UI_Scene.Editor, disabledText = "Inactive")]
        [KSPField(guiName = "Roll", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        public bool rollaxis = true;

        [UI_Toggle(enabledText = "True", scene = UI_Scene.Editor, disabledText = "False")]
        [KSPField(guiName = "Use as flap", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        public bool isFlap;

        [UI_Toggle(enabledText = "True", scene = UI_Scene.Editor, disabledText = "False")]
        [KSPField(guiName = "Use as spoiler", isPersistant = true, guiActiveEditor = true, guiActive = false)]
        public bool isSpoiler;

        [KSPField(isPersistant = true, guiName = "Flap deflection")]
        public int flapDeflectionLevel = 2;

        [UI_FloatRange(maxValue = 30, minValue = -15, scene = UI_Scene.Editor, stepIncrement = 0.5f)]
        [KSPField(guiName = "Control Max Deflection", isPersistant = true)]
        public float maxdeflect = 15;

        [UI_FloatRange(maxValue = 85, minValue = -30, scene = UI_Scene.Editor, stepIncrement = 0.5f)]
        [KSPField(guiName = "Flap/Brake Max Deflection", isPersistant = true)]
        public float maxdeflectFlap = 15; 
        
        protected float PitchLocation = 0;
        protected float YawLocation = 0;
        protected float RollLocation = 0;
        protected int flapLocation = 0;

        private float AoAsign = 1;
        private float AoAdesired = 0;
        private float AoAfromflap = 0;
        protected float AoAoffset = 0;

        private float lastAoAoffset = 0;
        private Vector3 deflectedNormal = Vector3.forward;

        public static float timeConstant = 0.05f;
        private bool brake = false;
        private bool justStarted = false;


        [KSPAction("Activate Spoiler", actionGroup = KSPActionGroup.Brakes)]
        public void ActivateSpoiler(KSPActionParam param)
        {
            OnSpoilerActivate();
        }

        private void OnSpoilerActivate()
        {
            brake = !brake;
        }

        [KSPAction("Increase Flap Deflection")]
        public void IncreaseDeflect(KSPActionParam param)
        {
            param.Cooldown = 0.25f;
            SetDeflection(flapDeflectionLevel + 1);
        }

        [KSPEvent(name = "DeflectMore", active = false, guiActive = true, guiName = "Deflect more")]
        public void DeflectMore()
        {
            SetDeflection(flapDeflectionLevel + 1);
            UpdateFlapDeflect();
        }

        [KSPAction("Decrease Flap Deflection")]
        public void DecreaseDeflect(KSPActionParam param)
        {
            param.Cooldown = 0.25f;
            SetDeflection(flapDeflectionLevel - 1);
        }

        [KSPEvent(name = "DeflectLess", active = false, guiActive = true, guiName = "Deflect less")]
        public void DeflectLess()
        {
            SetDeflection(flapDeflectionLevel - 1);
            UpdateFlapDeflect();
        }

        private void UpdateFlapDeflect()
        {
            foreach (Part p in part.symmetryCounterparts)
                foreach (PartModule m in p.Modules)
                    if (m is FARControllableSurface)
                        (m as FARControllableSurface).SetDeflection(this.flapDeflectionLevel);
        }

        private void SetDeflection(int newstate)
        {
            flapDeflectionLevel = Math.Max(0, Math.Min(3, newstate));
            UpdateEvents();
        }

        public void UpdateEvents()
        {
            Fields["flapDeflectionLevel"].guiActive = isFlap;
            Events["DeflectMore"].active = isFlap && flapDeflectionLevel < 3;
            Events["DeflectLess"].active = isFlap && flapDeflectionLevel > 0;
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (part.Modules.Contains("ModuleControlSurface"))
            {
                part.RemoveModule(part.Modules["ModuleControlSurface"]);
            }

            OnVesselPartsChange += CalculateSurfaceFunctions;
            UpdateEvents();
            justStarted = true;
        }

        public override void FixedUpdate()
        {
            if (justStarted)
                CalculateSurfaceFunctions();

            if (start != StartState.Editor && (object)part != null && (object)vessel != null)
            {
                bool process = part.isControllable || (justStarted && isFlap);

                if (process && (object)MovableSection != null && part.Rigidbody)
                {
                    if (isFlap == true)
                        AoAOffsetFromFlapDeflection();
                    else if (isSpoiler == true)
                        AoAOffsetFromSpoilerDeflection();
                    
                    AoAOffsetFromControl();

                }
            }

            base.FixedUpdate();
            justStarted = false;
        }

        #region Deflection

        public void CalculateSurfaceFunctions()
        {
            if (start != StartState.Editor && (object)vessel == null)
                return;

            if (isFlap == true)
            {
                if (start != StartState.Editor)
                    flapLocation = (int)Mathf.Sign(Vector3.Dot(vessel.ReferenceTransform.forward, part.transform.forward));      //figure out which way is up
                else
                    flapLocation = (int)Mathf.Sign(Vector3.Dot(EditorLogic.startPod.transform.forward, part.transform.forward));      //figure out which way is up
            }
            else if (isSpoiler == true)
            {
                if (start != StartState.Editor)
                    flapLocation = -(int)Mathf.Sign(Vector3.Dot(vessel.ReferenceTransform.forward, part.transform.forward));      //figure out which way is up
                else
                    flapLocation = -(int)Mathf.Sign(Vector3.Dot(EditorLogic.startPod.transform.forward, part.transform.forward));      //figure out which way is up
            }

            if(pitchaxis || yawaxis || rollaxis || start == StartState.Editor)
            {
                Vector3 CoM = Vector3.zero;
                float mass = 0;
                foreach (Part p in VesselPartList)
                {
                    CoM += p.transform.position * p.mass;
                    mass += p.mass;
                }

                CoM /= mass;

                if (start == StartState.Editor && (isFlap || isSpoiler))
                    SetControlStateEditor(CoM, 0, 0, 0, FAREditorGUI.CurrentEditorFlapSetting, FAREditorGUI.CurrentEditorSpoilerSetting);

                float roll2 = 0;
                if (start == StartState.Editor)
                {
                    Vector3 CoMoffset = (part.transform.position - CoM).normalized;
                    PitchLocation = Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, EditorLogic.startPod.transform.up));
                    YawLocation = -Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.right) * Mathf.Sign(Vector3.Dot(CoMoffset, EditorLogic.startPod.transform.up));
                    RollLocation = Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, -EditorLogic.startPod.transform.right));
                    roll2 = Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.right) * Mathf.Sign(Vector3.Dot(CoMoffset, EditorLogic.startPod.transform.forward));
                }
                else
                {
                    //Figures out where the ctrl surface is; this must be done after physics starts to get vessel COM
                    Vector3 CoMoffset = (part.transform.position - CoM).normalized;
                    PitchLocation = Vector3.Dot(part.transform.forward, vessel.ReferenceTransform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, vessel.ReferenceTransform.up));
                    YawLocation = -Vector3.Dot(part.transform.forward, vessel.ReferenceTransform.right) * Mathf.Sign(Vector3.Dot(CoMoffset, vessel.ReferenceTransform.up));
                    RollLocation = Vector3.Dot(part.transform.forward, vessel.ReferenceTransform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, -vessel.ReferenceTransform.right));
                    roll2 = Vector3.Dot(part.transform.forward, vessel.ReferenceTransform.right) * Mathf.Sign(Vector3.Dot(CoMoffset, vessel.ReferenceTransform.forward));
                    AoAsign = Mathf.Sign(Vector3.Dot(part.transform.up, vessel.transform.up));
                }
                //PitchLocation *= PitchLocation * Mathf.Sign(PitchLocation);
                //YawLocation *= YawLocation * Mathf.Sign(YawLocation);
                //RollLocation = RollLocation * RollLocation * Mathf.Sign(RollLocation) + roll2 * roll2 * Mathf.Sign(roll2);
                RollLocation += roll2;
            }
        }

        private void AoAOffsetFromSpoilerDeflection()
        {
            if (brake)
                AoAfromflap = maxdeflectFlap * flapLocation;
            else
                AoAfromflap = 0;

            AoAfromflap = Mathf.Clamp(AoAfromflap, -Mathf.Abs(maxdeflectFlap), Mathf.Abs(maxdeflectFlap));
        }

        
        private void AoAOffsetFromFlapDeflection()
        {
            AoAfromflap = maxdeflectFlap * flapLocation * flapDeflectionLevel * 0.33333333333f;
            AoAfromflap = Mathf.Clamp(AoAfromflap, -Mathf.Abs(maxdeflectFlap), Mathf.Abs(maxdeflectFlap));
        }

        private void AoAOffsetFromControl()
        {
            AoAdesired = 0;
            if ((object)vessel != null && vessel.staticPressure > 0)
            {
                if (pitchaxis)
                {
                    AoAdesired += PitchLocation * vessel.ctrlState.pitch;
                }
                if (yawaxis)
                {
                    AoAdesired += YawLocation * vessel.ctrlState.yaw;
                }
                if (rollaxis)
                {
                    AoAdesired += RollLocation * vessel.ctrlState.roll;
                }

                AoAdesired *= AoAsign * maxdeflect;
                AoAdesired = Mathf.Clamp(AoAdesired, -Mathf.Abs(maxdeflect), Mathf.Abs(maxdeflect));
                AoAdesired += AoAfromflap;
            }
            ChangeDeflection(timeConstant);

            DeflectionAnimation();
        }

        protected override float CalculateAoA(Vector3 velocity)
        {
            // Use the vector computed by DeflectionAnimation
            Vector3 perp = part_transform.TransformDirection(deflectedNormal);
            float PerpVelocity = Vector3.Dot(perp, velocity.normalized);
            return Mathf.Asin(PerpVelocity);
        }

        // Had to add this one since the parent class don't use AoAoffset and adding it would break GetWingInFrontOf
        public float CalculateAoA(Vector3 velocity, float AoAoffset)
        {
            float radAoAoffset = AoAoffset * Mathf.Deg2Rad * ctrlSurfFrac;
            Vector3 perp = part_transform.TransformDirection(new Vector3(0, FARMathUtil.FastSin(radAoAoffset), FARMathUtil.FastCos(radAoAoffset)));
            float PerpVelocity = Vector3.Dot(perp, velocity.normalized);
            return Mathf.Asin(PerpVelocity);
        }

        private void ChangeDeflection(float timeconstant)
        {
            if (AoAoffset != AoAdesired)
            {
                float error = AoAdesired - AoAoffset;
                if (!justStarted && Mathf.Abs(error) >= 0.5f)
                {
                    float recip_timeconstant = 1 / timeconstant;
                    float tmp1 = error * recip_timeconstant;
                    //float tmp2 = (error + TimeWarp.deltaTime * tmp1) * recip_timeconstant;
                    AoAoffset += Mathf.Clamp(TimeWarp.deltaTime * tmp1, -Mathf.Abs(0.6f * error), Mathf.Abs(0.6f * error));

                }
                else
                    AoAoffset = AoAdesired;
            }
        }

        /// <summary>
        /// This animates a deflection;
        /// </summary>
        protected void DeflectionAnimation()
        {
            // Don't recalculate on insignificant variations
            if (Mathf.Abs(lastAoAoffset - AoAoffset) < 0.01f)
                return;

            lastAoAoffset = AoAoffset;

            // Compute a vector for CalculateAoA
            float radAoAoffset = AoAoffset * Mathf.Deg2Rad * ctrlSurfFrac;
            deflectedNormal = new Vector3(0, FARMathUtil.FastSin(radAoAoffset), FARMathUtil.FastCos(radAoAoffset));

            // Visually animate the surface
            MovableSection.localRotation = MovableOrig;
            if (AoAoffset != 0)
            {
                if(flipAxis)
                    MovableSection.Rotate(controlSurfacePivot, AoAoffset);
                else
                    MovableSection.Rotate(controlSurfacePivot, -AoAoffset);
            }
        }

        public void SetControlStateEditor(Vector3 CoM, float pitch, float yaw, float roll, int flap, bool brake)
        {
            if (start == StartState.Editor)
            {
                Vector3 CoMoffset = (part.transform.position - CoM).normalized;
                PitchLocation = Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, EditorLogic.startPod.transform.up));
                YawLocation = -Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.right) * Mathf.Sign(Vector3.Dot(CoMoffset, EditorLogic.startPod.transform.up));
                RollLocation = Vector3.Dot(part.transform.forward, EditorLogic.startPod.transform.forward) * Mathf.Sign(Vector3.Dot(CoMoffset, -EditorLogic.startPod.transform.right));
                AoAoffset = 0;
                if (pitchaxis == true)
                {
                    AoAoffset += PitchLocation * pitch;
                }
                if (yawaxis == true)
                {
                    AoAoffset += YawLocation * yaw;
                }
                if (rollaxis == true)
                {
                    AoAoffset += RollLocation * roll;
                }
                AoAoffset = Mathf.Clamp(AoAoffset, -1, 1) * maxdeflect;

                if (isFlap == true)
                {
                    int flapDeflectionLevel = flap;
                    flapLocation = (int)Mathf.Sign(Vector3.Dot(EditorLogic.startPod.transform.forward, part.transform.forward));      //figure out which way is up
                    AoAoffset += maxdeflectFlap * flapLocation * flapDeflectionLevel * 0.3333333333333f;
                }
                else if (isSpoiler == true)
                {
                    flapLocation = -(int)Mathf.Sign(Vector3.Dot(EditorLogic.startPod.transform.forward, part.transform.forward));      //figure out which way is up
                    AoAoffset += brake ? maxdeflectFlap * flapLocation : 0;
                }

                DeflectionAnimation();
            }

        }
        #endregion
    }
}
