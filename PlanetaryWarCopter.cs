using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.Utils;
using VRage.Game.VisualScripting;
using VRage.Scripting;
using VRageMath;
using static PlanetaryWarCopter.Program;
using static Sandbox.Game.World.MyWorldGenerator;
using static System.BitStreamExtensions;
using static System.Net.WebRequestMethods;
using static System.Reflection.Metadata.BlobBuilder;
using static VRage.Game.MyObjectBuilder_ControllerSchemaDefinition;

namespace PlanetaryWarCopter
{
    public sealed class Program : MyGridProgram
    {




        IMyShipController Controller;
        IMyTextPanel LCD;
        MyDetectedEntityInfo Target;
        double DistanceToTarget;
        Vector3D DropPoint;
        bool AligningEnabled;

        #region Настройки
        public readonly double ScanRange = 5000;
        public readonly float AcceptableMovingAccuracy = 0.5f;
        public readonly float GyroMult = 0.5f;

        #endregion

        #region Переменные для наименований блоков и групп блоков

        public readonly string RotorBaseName = "Ротор Камера Горизонт Бомбер";
        public readonly string RotorAdjName = "Ротор Камера Вертикаль Бомбер";
        public readonly string CameraName = "Камера Обзор Бомбер";
        public readonly string LCDName = "Экран Бомбер";
        public readonly string ControllerName = "ДУ Бомбер";
        public readonly string MergeBlockGroupName = "Соединители Бомбер";
        public readonly string GyroGroupName = "Гироскопы Бомбер";
        public readonly string ThrustersGroupName = "Ускорители Бомбер";

        #endregion


        private static Program myScript;
        ScanningHandler scanningHandler;
        BombingHandler bombingHandler;
        FlightHandler flightHandler;

        public Program()
        {
            myScript = this;
            

            Controller = GridTerminalSystem.GetBlockWithName(ControllerName) as IMyShipController;
            LCD = GridTerminalSystem.GetBlockWithName(LCDName) as IMyTextPanel;

            scanningHandler = new ScanningHandler(Controller, LCD);
            bombingHandler = new BombingHandler(Controller, LCD);
            flightHandler = new FlightHandler(Controller, LCD);

            Runtime.UpdateFrequency = UpdateFrequency.Update1;


        }

        /// <summary>
        /// Запуск программного блока, выбор стартового режима
        /// </summary>
        /// <param name="arg">Аргумент запуска</param>
        public void Main(string arg)
        {
            switch (arg)
            {
                case "Scan":
                    if (scanningHandler.Scan(ScanRange))
                    {
                        DistanceToTarget = (Target.HitPosition.Value - DropPoint).Length();
                        LCD.WriteText($"{Target.Name}\n", false);
                        LCD.WriteText($"Дистанция: {DistanceToTarget:F}\n" , true);
                        bombingHandler.PrintTime();
                    }
                    else
                    {
                        LCD.WriteText("Цель не найдена", false);
                    }
                    break;
                case "Fire":
                    if (!Target.IsEmpty())
                    {
                        bombingHandler.Fire();
                    }
                    break;
                case "Armed":
                    bombingHandler.SetWarheadsArmed(true);
                    break;
                case "Disarmed":
                    bombingHandler.SetWarheadsArmed(false);
                    break;
                case "Go":
                    flightHandler.IsMoving = true;
                    break;
                case "Start":
                    AligningEnabled = true;
                    break;
                case "Stop":
                    flightHandler.IsMoving = false;
                    AligningEnabled = false;
                    flightHandler.StopAllGyros();
                    flightHandler.StopAllThrusters();
                    break;
                default:
                    break;

            }
            scanningHandler.ControlCamera();
            if (AligningEnabled)
            {
                flightHandler.GravitationAligning();
            }
            if (flightHandler.IsMoving && !DropPoint.IsZero())
            {
                flightHandler.IsMoving = !flightHandler.MovementOnVectorLinear(DropPoint, 30, false);
            }
        }

        


        public class ScanningHandler
        {
            private readonly IMyShipController _controller;
            private readonly IMyTextPanel _lcd;
            private readonly IMyMotorStator RotorBase, RotorAdj;
            private readonly IMyCameraBlock Camera;
            private readonly Vector3D PlanetCenter;


            public ScanningHandler(IMyShipController controller, IMyTextPanel lcd)
            {
                _controller = controller;
                _lcd = lcd;
                RotorBase = myScript.GridTerminalSystem.GetBlockWithName(myScript.RotorBaseName) as IMyMotorStator;
                RotorAdj = myScript.GridTerminalSystem.GetBlockWithName(myScript.RotorAdjName) as IMyMotorStator;
                Camera = myScript.GridTerminalSystem.GetBlockWithName(myScript.CameraName) as IMyCameraBlock;
                Camera.EnableRaycast = true;
                _controller.TryGetPlanetPosition(out PlanetCenter);
            }


            public void ControlCamera()
            {
                RotorBase.TargetVelocityRPM = -myScript.Controller.RotationIndicator.Y;
                RotorAdj.TargetVelocityRPM = myScript.Controller.RotationIndicator.X;
            }

            public bool Scan(double range)
            {
                myScript.Target = Camera.Raycast(range, 0, 0);
                if (!myScript.Target.IsEmpty())
                {
                    myScript.DropPoint = GetDropPoint((Vector3D)myScript.Target.HitPosition);
                    myScript.flightHandler.ForwardVector = Vector3D.Normalize((Vector3D)myScript.Target.HitPosition - _controller.GetPosition());
                }
                return !myScript.Target.IsEmpty();
            }

            private Vector3D GetDropPoint(Vector3D targetCoords)
            {
                double elevationAboveSurface = 0;
                _controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationAboveSurface);
                Vector3D dropPoint = Vector3D.Normalize(targetCoords - PlanetCenter) * elevationAboveSurface + targetCoords;
                return dropPoint;
            }

        }

        public class BombingHandler
        {
            private readonly IMyBlockGroup MergeBlockGroup;
            private readonly IMyShipController _controller;
            private readonly IMyTextPanel _lcd;
            private List<IMyShipMergeBlock> mergeBlocks;
            private List<IMyWarhead> warheads;

            public BombingHandler(IMyShipController controller, IMyTextPanel lcd)
            {
                _controller = controller;
                _lcd = lcd;
                mergeBlocks = new List<IMyShipMergeBlock>();
                MergeBlockGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.MergeBlockGroupName);
                MergeBlockGroup.GetBlocksOfType(mergeBlocks);
                
                warheads = new List<IMyWarhead>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyWarhead>(warheads);
            }

            public void PrintTime()
            {
                var time = CalculateFlightTime(myScript.DistanceToTarget, _controller.GetNaturalGravity().Length());
                _lcd.WriteText($"Время полета: {time:F}\n", true);
            }

            public void SetDetonationTimeToWarheads()
            {
                var dropTime = CalculateFlightTime(myScript.DistanceToTarget, _controller.GetNaturalGravity().Length());
                foreach (var warhead in warheads)
                {
                    warhead.DetonationTime = dropTime;
                }
            }
            private float CalculateFlightTime(double distance, double grav)
            {
                return (float)Math.Sqrt((2 * distance) / grav);
            }

            public void Fire()
            {
                if (mergeBlocks.Count > 0)
                {
                    mergeBlocks[0].Enabled = false;
                    mergeBlocks.RemoveAt(0);
                }
            }

            public void SetWarheadsArmed(bool armed)
            {
                foreach (var warhead in warheads)
                {
                    warhead.IsArmed = armed;
                }
            }
        }

        public class FlightHandler
        {
            

            private readonly IMyBlockGroup GyroscopesGroup, ThrustersGroup;
            private readonly IMyShipController _controller;
            private readonly IMyTextPanel _lcd;
            private readonly List<IMyThrust> ThrForward = new List<IMyThrust>();
            private readonly List<IMyThrust> ThrBackward = new List<IMyThrust>();
            private readonly List<IMyThrust> ThrRight = new List<IMyThrust>();
            private readonly List<IMyThrust> ThrLeft = new List<IMyThrust>();
            private readonly List<IMyThrust> ThrUp = new List<IMyThrust>();
            private readonly List<IMyThrust> ThrDown = new List<IMyThrust>();
            private readonly double ForwardThrustEff = 0;
            private readonly double BackwardThrustEff = 0;
            private readonly double RightThrustEff = 0;
            private readonly double LeftThrustEff = 0;
            private readonly double UpThrustEff = 0;
            private readonly double DownThrustEff = 0;
            public Vector3D ForwardVector;
            List<IMyGyro> Gyroscopes;
            public bool IsMoving;

            public FlightHandler(IMyShipController controller, IMyTextPanel lcd)
            {
                _controller = controller;
                _lcd = lcd;
                Gyroscopes = new List<IMyGyro>();
                GyroscopesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.GyroGroupName);
                GyroscopesGroup.GetBlocksOfType(Gyroscopes);

                //Инциализация двигателей по направлениям
                Matrix RemConMatrix = new Matrix();
                _controller.Orientation.GetMatrix(out RemConMatrix);
                Matrix ThrMatrix = new Matrix();
                List<IMyThrust> ThrTemp = new List<IMyThrust>();
                ThrustersGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.ThrustersGroupName);
                ThrustersGroup.GetBlocksOfType(ThrTemp);
                foreach (IMyThrust thr in ThrTemp)
                {
                    thr.Orientation.GetMatrix(out ThrMatrix);
                    if (ThrMatrix.Forward == RemConMatrix.Backward)
                    {
                        ThrForward.Add(thr);
                        ForwardThrustEff += thr.MaxEffectiveThrust;
                    }
                    else if (ThrMatrix.Forward == RemConMatrix.Forward)
                    {
                        ThrBackward.Add(thr);
                        BackwardThrustEff += thr.MaxEffectiveThrust;
                    }
                    else if (ThrMatrix.Forward == RemConMatrix.Left)
                    {
                        ThrRight.Add(thr);
                        RightThrustEff += thr.MaxEffectiveThrust;
                    }
                    else if (ThrMatrix.Forward == RemConMatrix.Right)
                    {
                        ThrLeft.Add(thr);
                        LeftThrustEff += thr.MaxEffectiveThrust;
                    }
                    else if (ThrMatrix.Forward == RemConMatrix.Down)
                    {
                        ThrUp.Add(thr);
                        UpThrustEff += thr.MaxEffectiveThrust;
                    }
                    else if (ThrMatrix.Forward == RemConMatrix.Up)
                    {
                        ThrDown.Add(thr);
                        DownThrustEff += thr.MaxEffectiveThrust;
                    }
                }                
            }

           

            public void GravitationAligning()
            {
                Vector3D gravVectorNorm = Vector3D.Normalize(_controller.GetNaturalGravity());
                Vector3D axisGrav = gravVectorNorm.Cross(_controller.WorldMatrix.Down);
                if (axisGrav.Dot(_controller.WorldMatrix.Down) < 0)
                {
                    axisGrav = Vector3D.Normalize(axisGrav);
                }
                Vector3D currentForwardVector = Vector3D.Reject(ForwardVector, gravVectorNorm);
                Vector3D axisForward = currentForwardVector.Cross(_controller.WorldMatrix.Forward);
                if (currentForwardVector.Dot(_controller.WorldMatrix.Forward) < 0)
                {
                    axisForward = Vector3D.Normalize(axisForward);
                }

                float pitch = (float)axisGrav.Dot(_controller.WorldMatrix.Right);
                float roll = (float)axisGrav.Dot(_controller.WorldMatrix.Backward);
                float yaw = (float)axisForward.Dot(_controller.WorldMatrix.Up);
                if (ForwardVector.IsZero() || !IsMoving)
                {
                    yaw = 0;
                }

                //myScript.Echo($"forVec = {myScript.GetVectorString(ForwardVector, "")}");
                //myScript.Echo($"pitch = {pitch}");
                //myScript.Echo($"roll = {roll}");
                //myScript.Echo($"yaw = {yaw}");

                foreach (IMyGyro gyro in Gyroscopes)
                {
                    gyro.GyroOverride = true;
                    //gyro.Pitch = Math.Abs(pitch) > 0.1 ? pitch * myScript.GyroMult : 0;
                    //gyro.Roll = Math.Abs(roll) > 0.1 ? roll * myScript.GyroMult : 0;
                    //gyro.Yaw = Math.Abs(yaw) > 0.1 ? yaw * myScript.GyroMult : 0;
                    gyro.Pitch = pitch;
                    gyro.Roll = roll;
                    gyro.Yaw = yaw;
                }
            }

            public void SetGyrosOverride(bool overrideControls)
            {
                foreach (IMyGyro gyro in Gyroscopes)
                {
                    gyro.GyroOverride = overrideControls;
                }
            }
            public void StopAllGyros()
            {
                foreach (IMyGyro gyro in Gyroscopes)
                {
                    gyro.Pitch = 0;
                    gyro.Roll = 0;
                    gyro.Yaw = 0;
                    gyro.GyroOverride = false;
                }
            }
            public bool MovementOnVectorLinear(Vector3D target, float speedLimit, bool horizontalAligmentFirst)
            {

                _controller.DampenersOverride = true;
                Vector3D linearVelocity = _controller.GetShipVelocities().LinearVelocity;
                if (linearVelocity.Length() < speedLimit)
                {

                    Vector3D pathVector = target - _controller.GetPosition();
                    Vector3D pathVectorForward = _controller.WorldMatrix.Forward * pathVector.Dot(_controller.WorldMatrix.Forward);
                    float ForwardScalar = (float)Vector3D.Normalize(pathVectorForward).Dot(_controller.WorldMatrix.Forward);

                    Vector3D pathVectorRight = _controller.WorldMatrix.Right * pathVector.Dot(_controller.WorldMatrix.Right);
                    float RightScalar = (float)Vector3D.Normalize(pathVectorRight).Dot(_controller.WorldMatrix.Right);

                    Vector3D pathVectorUp = _controller.WorldMatrix.Up * pathVector.Dot(_controller.WorldMatrix.Up);
                    float UpScalar = (float)Vector3D.Normalize(pathVectorUp).Dot(_controller.WorldMatrix.Up);

                    if (linearVelocity.Length() < myScript.AcceptableMovingAccuracy / 2 && (pathVectorForward.Length() + pathVectorRight.Length() + pathVectorUp.Length()) / 3 < myScript.AcceptableMovingAccuracy)
                    {
                        StopAllThrusters();
                        return true;
                    }

                    float shipMass = _controller.CalculateShipMass().PhysicalMass;

                    Vector3D velocityForward = _controller.WorldMatrix.Forward * linearVelocity.Dot(_controller.WorldMatrix.Forward);
                    Vector3D velocityRight = _controller.WorldMatrix.Right * linearVelocity.Dot(_controller.WorldMatrix.Right);
                    Vector3D velocityUp = _controller.WorldMatrix.Up * linearVelocity.Dot(_controller.WorldMatrix.Up);

                    float forwardVelScalar = (float)velocityForward.Dot(_controller.WorldMatrix.Forward);
                    float stopDistForward = (float)(0.5 * shipMass * Math.Pow(forwardVelScalar, 2) / (forwardVelScalar > 0 ? BackwardThrustEff : ForwardThrustEff));
                    float rightVelScalar = (float)velocityRight.Dot(_controller.WorldMatrix.Right);
                    float stopDistRight = (float)(0.5 * shipMass * Math.Pow(rightVelScalar, 2) / (rightVelScalar > 0 ? LeftThrustEff : RightThrustEff));
                    float upVelScalar = (float)velocityUp.Dot(_controller.WorldMatrix.Up);
                    float stopDistUp = (float)(0.5 * shipMass * Math.Pow(upVelScalar, 2) / (upVelScalar > 0 ?
                                                                                                DownThrustEff + (shipMass * _controller.GetNaturalGravity().Length()) :
                                                                                                UpThrustEff - (shipMass * _controller.GetNaturalGravity().Length())));


                    if (pathVectorForward.Length() > stopDistForward && pathVectorForward.Length() > myScript.AcceptableMovingAccuracy
                        && (horizontalAligmentFirst || pathVectorUp.Length() < myScript.AcceptableMovingAccuracy))
                    {
                        SetAxisThrustsByScalar(ThrForward, ThrBackward, ForwardScalar);
                    }
                    else
                    {
                        SetTrustersPercentage(ThrForward, 0);
                        SetTrustersPercentage(ThrBackward, 0);
                    }

                    if (pathVectorRight.Length() > stopDistRight && pathVectorRight.Length() > myScript.AcceptableMovingAccuracy
                        && (horizontalAligmentFirst || pathVectorUp.Length() < myScript.AcceptableMovingAccuracy))
                    {
                        SetAxisThrustsByScalar(ThrRight, ThrLeft, RightScalar);
                    }
                    else
                    {
                        SetTrustersPercentage(ThrRight, 0);
                        SetTrustersPercentage(ThrLeft, 0);
                    }

                    if (pathVectorUp.Length() > stopDistUp && pathVectorUp.Length() > myScript.AcceptableMovingAccuracy
                        && (!horizontalAligmentFirst || (pathVectorForward.Length() < myScript.AcceptableMovingAccuracy
                        && pathVectorRight.Length() < myScript.AcceptableMovingAccuracy)))
                    {
                        if (UpScalar > 0)
                        {
                            SetTrustersPercentage(ThrUp, 1);
                        }
                        else
                        {
                            float keepElevationT = (float)(shipMass * _controller.GetNaturalGravity().Length());
                            if (-upVelScalar < speedLimit * 0.95f && -upVelScalar > speedLimit)
                            {
                                SetTrustersNewtons(ThrUp, keepElevationT);
                            }
                            else
                            {
                                float coeff = 10.555f / (speedLimit + 11.11f); // Расчет обратно-пропорционального коэффициента
                                SetTrustersNewtons(ThrUp, keepElevationT * coeff);
                            }
                        }
                    }
                    else
                    {
                        SetTrustersPercentage(ThrUp, 0);
                        SetTrustersPercentage(ThrDown, 0);
                    }
                }
                else
                {
                    StopAllThrusters();
                }
                return false;
            }

            private void SetAxisThrustsByScalar(List<IMyThrust> thrPositive, List<IMyThrust> thrNegative, float scalar)
            {
                if (scalar > 0)
                {
                    SetTrustersPercentage(thrPositive, 1);
                }
                else
                {
                    SetTrustersPercentage(thrNegative, 1);
                }
            }

            public void StopAllThrusters()
            {
                SetTrustersPercentage(ThrForward, 0);
                SetTrustersPercentage(ThrBackward, 0);
                SetTrustersPercentage(ThrRight, 0);
                SetTrustersPercentage(ThrLeft, 0);
                SetTrustersPercentage(ThrUp, 0);
                SetTrustersPercentage(ThrDown, 0);
            }
            private void SetTrustersPercentage(List<IMyThrust> list, float value)
            {
                foreach (IMyThrust thrust in list)
                {
                    thrust.ThrustOverridePercentage = value;
                }
            }
            private void SetTrustersNewtons(List<IMyThrust> list, float value)
            {
                foreach (IMyThrust thrust in list)
                {
                    thrust.ThrustOverride = value / list.Count;
                }
            }
        }


        public string GetVectorString(Vector3D vector, string name, string colorHEX = "#FF00FF")
        {
            return $"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n";
        }








    }
}