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
        Vector3D CurrentDropPoint;
        IMyTextSurface pbText;


        //IMyTextPanel Display;


        #region Настройки
        public readonly double ScanRange = 5000;
        public readonly float AcceptableMovingAccuracy = 0.5f;
        public readonly float GyroMult = 0.5f;
        public readonly long SpotterAddress = 72970061186382229;

        #endregion

        #region Переменные для наименований блоков и групп блоков

        public readonly string CameraName = "Камера Вниз Бомбер";
        public readonly string ControllerName = "ДУ Бомбер";
        public readonly string MergeBlockGroupName = "Соединители Бомбер";

        //public readonly string DisplayName = "Экран Бомбер";

        #endregion


        private static Program myScript;
        ScanningHandler scanningHandler;
        BombingHandler bombingHandler;
        FlightHandler flightHandler;
        CommunicationHandler communicationHandler;

        public Program()
        {
            myScript = this;
            Controller = GridTerminalSystem.GetBlockWithName(ControllerName) as IMyShipController;
            //Display = GridTerminalSystem.GetBlockWithName(DisplayName) as IMyTextPanel;
            scanningHandler = new ScanningHandler();
            bombingHandler = new BombingHandler(Controller);
            flightHandler = new FlightHandler(Controller);
            communicationHandler = new CommunicationHandler();

            CurrentDropPoint = Vector3D.Zero;

            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            pbText = myScript.Me.GetSurface(0);
            pbText.WriteText(myScript.IGC.Me.ToString() + "\n");
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
                    scanningHandler.Scan(ScanRange);
                    break;
                case "Fire":
                    bombingHandler.Fire();
                    break;
                case "Armed":
                    bombingHandler.SetWarheadsArmed(true);
                    break;
                case "Disarmed":
                    bombingHandler.SetWarheadsArmed(false);
                    break;
                case "GoToCurrent":
                    flightHandler.GoToCurrent();
                    break;
                case "GoToNext"://TODO
                    flightHandler.IsMoving = true;
                    break;
                case "StartAlign":
                    flightHandler.AligningEnabled = true;
                    break;
                case "StopAlign":
                    flightHandler.IsMoving = false;
                    flightHandler.AligningEnabled = false;
                    flightHandler.StopAllGyros();
                    flightHandler.StopAllThrusters();
                    break;
                case "ProcessMessage":
                    communicationHandler.ProcessMessage();
                    break;

                case "Clear":
                    //Display.WriteText("");
                    break;
                default:
                    break;

            }

            
            if (flightHandler.AligningEnabled)
            {
                flightHandler.GravitationAligning();
            }
            if (flightHandler.IsMoving && !CurrentDropPoint.IsZero())
            {
                if (flightHandler.MovementOnVectorLinear(CurrentDropPoint, 30, false))
                {
                    myScript.communicationHandler.SendMessage("IsReadyToFire", true);
                    flightHandler.IsMoving = false;
                    flightHandler.AligningEnabled = false;
                    flightHandler.StopAllGyros();
                    flightHandler.StopAllThrusters();
                }
                else
                {
                    myScript.communicationHandler.SendMessage("DistanceToTarget", bombingHandler.GetDistanceToTarget(CurrentDropPoint));
                }
            }

        }

        


        public class ScanningHandler
        {
            private readonly IMyCameraBlock Camera;
            


            public ScanningHandler()
            {
                Camera = myScript.GridTerminalSystem.GetBlockWithName(myScript.CameraName) as IMyCameraBlock;
                Camera.EnableRaycast = true;
            }

            public bool Scan(double range)
            {
                var target = Camera.Raycast(range, 0, 0);
                if (!target.IsEmpty())
                {
                    var targetCoords = (Vector3D)target.HitPosition;
                    myScript.bombingHandler.AddTarget(targetCoords);
                    var targets = myScript.bombingHandler.GetTargets();
                    //myScript.Display.WriteText("");
                }
                return !target.IsEmpty();
            }
        }

        public class BombingHandler
        {
            private readonly Vector3D PlanetCenter;
            private readonly IMyBlockGroup _mergeBlockGroup;
            private readonly IMyShipController _controller;
            //private readonly IMyTextPanel _lcd;
            private List<IMyShipMergeBlock> _mergeBlocks;
            private List<IMyWarhead> _warheads;
            private List<Vector3D> _targets;

            public BombingHandler(IMyShipController controller)
            {
                _controller = controller;
                _mergeBlocks = new List<IMyShipMergeBlock>();
                _mergeBlockGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.MergeBlockGroupName);
                _mergeBlockGroup.GetBlocksOfType(_mergeBlocks);
                _controller.TryGetPlanetPosition(out PlanetCenter);
                //_lcd = myScript.Display;

                _warheads = new List<IMyWarhead>();
                _targets = new List<Vector3D>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyWarhead>(_warheads);
            }

            public void AddTarget(Vector3D targetCoords)
            {
                _targets.Add(targetCoords);
                myScript.communicationHandler.SendMessage("TargetsCount", _targets.Count);
            }

            public void MarkOffCurrentTarget()
            {
                if (_targets.Count > 0)
                {
                    _targets.RemoveAt(0);
                    myScript.communicationHandler.SendMessage("TargetsCount", _targets.Count);
                    var nextTarget = GetCurrentTarget();
                    if (nextTarget.IsZero())
                    {
                        myScript.CurrentDropPoint = Vector3D.Zero;
                        myScript.communicationHandler.SendMessage("DistanceToTarget", 0);
                    }
                    else 
                    {
                        myScript.CurrentDropPoint = GetDropPoint(nextTarget);
                        myScript.communicationHandler.SendMessage("DistanceToTarget", GetDistanceToTarget(myScript.CurrentDropPoint));
                    }
                    
                }
            }

            public Vector3D GetCurrentTarget()
            {
                return _targets.Count > 0 ? _targets[0] : Vector3D.Zero;
            }

            public List<Vector3D> GetTargets()
            {  
                return _targets; 
            }

            public void ProccessTarget(Vector3D target)
            {
                _targets.Add(target);
                myScript.communicationHandler.SendMessage("TargetsCount", _targets.Count);
                if (_targets.Count == 1)
                {
                    myScript.CurrentDropPoint = GetDropPoint(target);
                    myScript.communicationHandler.SendMessage("DistanceToTarget", GetDistanceToTarget(myScript.CurrentDropPoint));
                }
            }

            public Vector3D GetDropPoint(Vector3D targetCoords)
            {
                double elevationAboveSurface = 0;
                _controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationAboveSurface);
                //myScript.Display.WriteText($"elevationAboveSurface = {elevationAboveSurface}\n", true);
                //myScript.Display.WriteText(myScript.GetVectorString(PlanetCenter, "PlanetCenter"), true);
                //myScript.Display.WriteText(myScript.GetVectorString(targetCoords, "targetCoords"), true);
                //myScript.Display.WriteText(myScript.GetVectorString(Vector3D.Normalize(targetCoords - PlanetCenter), "targetCoords - PlanetCenter"), true);
                Vector3D dropPoint = (Vector3D.Normalize(targetCoords - PlanetCenter) * ((targetCoords - PlanetCenter).Length() + elevationAboveSurface)) + PlanetCenter;
                myScript.PrintVector(dropPoint, "DropPoint", false);
                myScript.PrintVector(PlanetCenter, "PlanetCenter", true);
                myScript.PrintVector(targetCoords, "targetCoords", true);
                myScript.pbText.WriteText($"elevationAboveSurface = {elevationAboveSurface}\n", true);
                return dropPoint;
            }

            public double GetDistanceToTarget(Vector3D target)
            {
                return (_controller.GetPosition() - target).Length();
            }

            private float CalculateFlightTime(double distance, double grav)
            {
                return (float)Math.Sqrt((2 * distance) / grav);
            }

            public void Fire()
            {
                if (_mergeBlocks.Count > 0)
                {
                    _mergeBlocks[0].Enabled = false;
                    _mergeBlocks.RemoveAt(0);
                }
            }

            public void SetWarheadsArmed(bool armed)
            {
                foreach (var warhead in _warheads)
                {
                    warhead.IsArmed = armed;
                }
            }
        }

        public class FlightHandler
        {
            

            private readonly IMyShipController _controller;
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
            public bool AligningEnabled;

            public FlightHandler(IMyShipController controller)
            {
                _controller = controller;
                Gyroscopes = new List<IMyGyro>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyGyro>(Gyroscopes, (a) => a.IsSameConstructAs(_controller));

                //Инциализация двигателей по направлениям
                Matrix RemConMatrix = new Matrix();
                _controller.Orientation.GetMatrix(out RemConMatrix);
                Matrix ThrMatrix = new Matrix();
                List<IMyThrust> ThrTemp = new List<IMyThrust>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyThrust>(ThrTemp, (a) => (a.IsSameConstructAs(_controller)));
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


            public void GoToCurrent()
            {
                if (myScript.bombingHandler.GetTargets().Count > 0)
                {
                    myScript.CurrentDropPoint = myScript.bombingHandler.GetDropPoint(myScript.bombingHandler.GetCurrentTarget());
                    ForwardVector = Vector3D.Normalize(myScript.CurrentDropPoint - _controller.GetPosition());
                    AligningEnabled = true;
                    IsMoving = true;
                }
                myScript.communicationHandler.SendMessage("IsReadyToFire", false);
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

                foreach (IMyGyro gyro in Gyroscopes)
                {
                    gyro.GyroOverride = true;
                    gyro.Pitch = (float)axisGrav.Dot(gyro.WorldMatrix.Right) * myScript.GyroMult;
                    gyro.Roll = (float)axisGrav.Dot(gyro.WorldMatrix.Backward) * myScript.GyroMult;
                    gyro.Yaw = (float)axisForward.Dot(gyro.WorldMatrix.Up) * myScript.GyroMult;
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

                    if (linearVelocity.Length() < myScript.AcceptableMovingAccuracy / 3 && (pathVectorForward.Length() + pathVectorRight.Length() + pathVectorUp.Length()) / 3 < myScript.AcceptableMovingAccuracy)
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


        private class CommunicationHandler
        {
            public CommunicationHandler()
            {
                myScript.IGC.UnicastListener.SetMessageCallback("ProcessMessage");
            }

            public void SendMessage(string command, object data)
            {
                var dateStr = string.Empty;
                if (data != null)
                {
                    if (data.GetType() == typeof(Vector3D))
                    {
                        dateStr = GetVectorString((Vector3D)data);
                    }
                    else
                    {
                        dateStr = data.ToString();
                    }
                }
                if (!myScript.IGC.SendUnicastMessage(myScript.SpotterAddress, command, dateStr))
                {
                    //myScript.Display.WriteText("Target wasn't delivered!\n", true);
                }
            }

            public void ProcessMessage()
            {
                while (myScript.IGC.UnicastListener.HasPendingMessage)
                {
                    var message = myScript.IGC.UnicastListener.AcceptMessage();
                    switch (message.Tag)
                    {
                        case "SendTarget":
                            var target = ParseVector3D((string)message.Data);
                            myScript.bombingHandler.ProccessTarget(target);
                            break;
                        case "GoToCurrent":
                            myScript.flightHandler.GoToCurrent();
                            break;
                        case "GoToNext":
                            myScript.bombingHandler.MarkOffCurrentTarget();
                            myScript.flightHandler.GoToCurrent();
                            break;
                        case "SetArmed":
                            myScript.bombingHandler.SetWarheadsArmed(true);
                            break;
                        case "Fire":
                            myScript.bombingHandler.Fire();
                            break;
                        default: 
                            break;
                    }
                }
            }

            public Vector3D ParseVector3D(string vectorStr)
            {
                Vector3D vector = new Vector3D();
                var coords = vectorStr.Split(':');
                if (coords.Length == 3)
                {
                    vector.X = Convert.ToDouble(coords[0]);
                    vector.Y = Convert.ToDouble(coords[1]);
                    vector.Z = Convert.ToDouble(coords[2]);
                }
                return vector;
            }

            public string GetVectorString(Vector3D vector)
            {
                return $"{vector.X.ToString()}:{vector.Y.ToString()}:{vector.Z.ToString()}";
            }

        }
        private void PrintVector(Vector3D vector, string name, bool append, string colorHEX = "#FF00FF")
        {
            myScript.pbText.WriteText($"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n", append);
        }















    }
}