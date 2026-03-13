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

        #endregion


        private static Program myScript;
        ScanningHandler scanningHandler;
        BombingHandler bombingHandler;

        public Program()
        {
            myScript = this;
            

            Controller = GridTerminalSystem.GetBlockWithName(ControllerName) as IMyShipController;
            LCD = GridTerminalSystem.GetBlockWithName(LCDName) as IMyTextPanel;

            scanningHandler = new ScanningHandler();
            bombingHandler = new BombingHandler(Controller, LCD);

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
                case "Fire":
                    if (!Target.IsEmpty())
                    {
                        bombingHandler.SetDetonationTimeToWarheads();
                        bombingHandler.Fire();
                    }
                    break;
                default:
                    break;

            }

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
            private readonly List<IMyShipMergeBlock> mergeBlocks;
            private readonly List<IMyWarhead> warheads;
            private readonly IMyShipController _controller;
            private readonly IMyTextPanel _lcd;

            public BombingHandler(IMyShipController controller, IMyTextPanel lcd)
            {
                _controller = controller;
                _lcd = lcd;
                mergeBlocks = new List<IMyShipMergeBlock>();
                MergeBlockGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.MergeBlockGroupName);
                if (mergeBlocks != null)
                {
                    MergeBlockGroup.GetBlocksOfType(mergeBlocks);
                }
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
                foreach (var warhead in warheads)
                {
                    warhead.IsArmed = true;
                    warhead.StartCountdown();
                }
                foreach (var mergeBlock in mergeBlocks)
                {
                    mergeBlock.Enabled = false;
                }
            }


        }

        public string GetVectorString(Vector3D vector, string name, string colorHEX = "#FF00FF")
        {
            return $"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n";
        }








    }
}