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

        #region Настройки
        public readonly double ScanRange = 5000;

        #endregion

        #region Переменные для наименований блоков и групп блоков

        public readonly string RotorBaseName = "Ротор Камера Горизонт";
        public readonly string RotorAdjName = "Ротор Камера Вертикаль";
        public readonly string CameraName = "Камера";
        public readonly string LCDName = "Экран";
        public readonly string ControllerName = "Кокпит";

        public readonly string MergeBlockGroupName = "Соединители Коптер";

        public readonly string GyroGroupName = "Гироскопы Коптер";
        public readonly string ThrustersGroupName = "Ускорители Коптер";

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

            scanningHandler = new ScanningHandler();
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
                        DistanceToTarget = (Target.HitPosition.Value - Controller.GetPosition()).Length();
                        LCD.WriteText($"{Target.Name}\n", false);
                        LCD.WriteText($"Дистанция: {DistanceToTarget:F}\n" , true);
                        bombingHandler.PrintTime();
                    }
                    else
                    {
                        LCD.WriteText("Цель не найдена", false);
                    }
                    break;
                case "FireOnce":
                    if (!Target.IsEmpty())
                    {
                        bombingHandler.Fire();
                    }
                    break;
                case "GetWarheadsArmed":
                    bombingHandler.SetWarheadsArmed(true);
                    break;
                case "GetWarheadsDisarmed":
                    bombingHandler.SetWarheadsArmed(false);
                    break;
                default:
                    break;

            }
            flightHandler.KeepHorizon();
            scanningHandler.ControlCamera();
           
        }

        


        public class ScanningHandler
        {
            private readonly IMyMotorStator RotorBase, RotorAdj;
            private readonly IMyCameraBlock Camera;


            public ScanningHandler()
            {
                RotorBase = myScript.GridTerminalSystem.GetBlockWithName(myScript.RotorBaseName) as IMyMotorStator;
                RotorAdj = myScript.GridTerminalSystem.GetBlockWithName(myScript.RotorAdjName) as IMyMotorStator;
                Camera = myScript.GridTerminalSystem.GetBlockWithName(myScript.CameraName) as IMyCameraBlock;
                Camera.EnableRaycast = true;
            }


            public void ControlCamera()
            {
                RotorBase.TargetVelocityRPM = -myScript.Controller.RotationIndicator.Y;
                RotorAdj.TargetVelocityRPM = -myScript.Controller.RotationIndicator.X;
            }

            public bool Scan(double range)
            {
                myScript.Target = Camera.Raycast(range, 0, 0);
                return !myScript.Target.IsEmpty();
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
            //3 переменные для поддержания высоты полета над поверхностью
            double HoverHeight = 0;
            double CurrentHeight = 0;
            double ForwardSpeed = 0;
            double ForwardVelocityOld = 0;
            double Faccel = 0;
            //Коэффициент Kv, характеризующий пропорциональную зависимость между разностью требуемой и текущей высот и необходимой вертикальной скоростью
            double Kv = 1;
            //Коэффициент Ka, характеризующий пропорциональную зависимость между разностью требуемой и текущей верт. скоростей и желаемым ускорением
            double Ka = 2.5;

            private readonly IMyBlockGroup gyroGroup, thrustersGroup;
            private readonly IMyShipController _controller;
            private readonly IMyTextPanel _lcd;
            List<IMyThrust> thrusters;
            List<IMyGyro> gyros;

            public FlightHandler(IMyShipController controller, IMyTextPanel lcd)
            {
                _controller = controller;
                _lcd = lcd;
                gyros = new List<IMyGyro>();
                gyroGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.GyroGroupName);
                gyroGroup.GetBlocksOfType(gyros);
                thrusters = new List<IMyThrust>();
                thrustersGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.ThrustersGroupName);
                thrustersGroup.GetBlocksOfType(thrusters);
            }

            public void KeepHorizon()
            {

                HoverHeight += _controller.MoveIndicator.Y / 10;
                float ShipMass = _controller.CalculateShipMass().PhysicalMass;

                Vector3D GravityVector = _controller.GetNaturalGravity();
                Vector3D GravNorm = Vector3D.Normalize(GravityVector);
                Vector3D ForwardVector = Vector3D.Normalize(Vector3D.Reject(_controller.WorldMatrix.Forward, GravNorm));
                Vector3D VelocityCompensator = Vector3D.Reject(_controller.GetShipVelocities().LinearVelocity, ForwardVector);
                if (VelocityCompensator.Length() > 10)
                {
                    VelocityCompensator = Vector3D.Normalize(VelocityCompensator) * 10;
                }
                Vector3D StopVector = VelocityCompensator / 10;


                //float ForwardInput = Controller.MoveIndicator.Z;
                ForwardSpeed += _controller.MoveIndicator.Z * 0.1;
                double ForwardVelocity = -_controller.GetShipVelocities().LinearVelocity.Dot(ForwardVector);
                Faccel = ForwardVelocity - ForwardVelocityOld;
                ForwardVelocityOld = ForwardVelocity;

                double ForwardSpeedFactor = ((ForwardSpeed - ForwardVelocity) * 0.1 - Faccel) * 0.5;

                Vector3D ForwardPart = ForwardVector * ForwardSpeedFactor;
                float YawInput = _controller.MoveIndicator.X;
                StopVector += _controller.WorldMatrix.Left * _controller.RollIndicator * 1.0f;
                StopVector += ForwardPart * 1.2f;
                if (StopVector.Length() > 1)
                {
                    StopVector = Vector3D.Normalize(StopVector);
                }
                StopVector += GravNorm;

                float RollInput = (float)StopVector.Dot(_controller.WorldMatrix.Left);
                float PitchInput = -(float)StopVector.Dot(_controller.WorldMatrix.Forward);

                foreach (IMyGyro Gyro in gyros)
                {
                    Gyro.Yaw = YawInput;
                    Gyro.Roll = RollInput;
                    Gyro.Pitch = PitchInput;
                }


                _controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out CurrentHeight);
                //if (CurrentHeight > myRadar.CritDepth) { CurrentHeight = myRadar.CritDepth; }
                double HeightDelta = HoverHeight - CurrentHeight;
                double VerticalSpeed = -_controller.GetShipVelocities().LinearVelocity.Dot(GravNorm);

                if (HeightDelta < 0)
                {
                    HeightDelta /= 10;
                }

                double HoverCorrection = (HeightDelta * Kv - VerticalSpeed) * Ka;
                myScript.Echo("HoverCorrection = " + HoverCorrection);

                float MyThrust = (float)(GravityVector.Length() * ShipMass * (1 + HoverCorrection) / GravNorm.Dot(_controller.WorldMatrix.Down));
                if (MyThrust <= 0)
                {
                    MyThrust = 1;
                }
                SetThrust(MyThrust);
            }

            public void SetThrust(float Thr)
            {
                foreach (IMyThrust thruster in thrusters)
                {
                    thruster.ThrustOverride = Thr / thrusters.Count;
                }
            }
        }


        public string GetVectorString(Vector3D vector, string name, string colorHEX = "#FF00FF")
        {
            return $"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n";
        }








    }
}