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
using static Spotter.Program;
using static Sandbox.Game.World.MyWorldGenerator;
using static System.BitStreamExtensions;
using static System.Net.WebRequestMethods;
using static System.Reflection.Metadata.BlobBuilder;
using static VRage.Game.MyObjectBuilder_ControllerSchemaDefinition;

namespace Spotter
{
    public sealed class Program : MyGridProgram
    {







        IMyTextPanel LCD;
        MyDetectedEntityInfo Target;
        IMyTextSurface pbText;

        #region Настройки
        public readonly double ScanRange = 10000;
        public readonly long BomberAddress = 132647153537962255;
        #endregion

        #region Переменные для наименований блоков и групп блоков

        public readonly string CameraName = "Камера Споттер";
        public readonly string LCDName = "Экран Споттер";
        //public readonly string LCD2Name = "Экран 2 Споттер";


        #endregion


        private static Program myScript;
        SpottingHandler spottingHandler;
        CommunicationHandler communicationHandler;


        public Program()
        {
            myScript = this;
            

            LCD = GridTerminalSystem.GetBlockWithName(LCDName) as IMyTextPanel;
            //LCD2 = GridTerminalSystem.GetBlockWithName(LCD2Name) as IMyTextPanel;

            spottingHandler = new SpottingHandler();
            communicationHandler = new CommunicationHandler();

            

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
                    if (spottingHandler.Scan(ScanRange))
                    {
                        var hitPos = (Vector3D)myScript.Target.HitPosition;
                        communicationHandler.SendMessage("SendTarget", hitPos);
                    }
                    break;
                case "GoToCurrent":
                    communicationHandler.SendMessage("GoToCurrent", "");
                    break;
                case "GoToNext":
                    communicationHandler.SendMessage("GoToNext", "");
                    break;
                case "SetArmed":
                    communicationHandler.SendMessage("SetArmed", "");
                    break;
                case "Fire":
                    communicationHandler.SendMessage("Fire", "");
                    break;
                case "Clear":
                    spottingHandler.Clear();
                    break;
                case "ProcessMessage":
                    communicationHandler.ProcessMessage();
                    break;
                default:
                    break;

            }

            LCD.WriteText(communicationHandler.GetTargetsInfo());
        }

        


        private class SpottingHandler
        {
            //private readonly IMyTextPanel _lcd;
            private readonly IMyCameraBlock _camera;


            public SpottingHandler()
            {
                //_lcd = lcd;
                _camera = myScript.GridTerminalSystem.GetBlockWithName(myScript.CameraName) as IMyCameraBlock;
                if (_camera != null)
                    _camera.EnableRaycast = true;
            }

            public bool Scan(double range)
            {
                myScript.Target = _camera.Raycast(range, 0, 0);
                return !myScript.Target.IsEmpty();
            }

            public void Clear()
            {
                //_lcd.WriteText("");
            }

            public void PrintVector(Vector3D vector, string name, bool append, string colorHEX = "#FF00FF")
            {
                //_lcd.WriteText($"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n", append);
            }

        }

        private class CommunicationHandler
        {
            private int targetscount = 0;
            private double distanceToTarget = 0;
            private bool isReadyToFire = false;

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
                if (!myScript.IGC.SendUnicastMessage(myScript.BomberAddress, command, dateStr))
                {
                    //myScript.LCD.WriteText("Target wasn't delivered!\n", true);
                }
            }

            public void ProcessMessage()
            {
                while (myScript.IGC.UnicastListener.HasPendingMessage)
                {
                    var message = myScript.IGC.UnicastListener.AcceptMessage();
                    switch (message.Tag)
                    {
                        case "TargetsCount":
                            targetscount = int.Parse((string)message.Data);
                            break;
                        case "DistanceToTarget":
                            distanceToTarget = double.Parse((string)message.Data);
                            break;
                        case "IsReadyToFire":
                            isReadyToFire = bool.Parse((string)message.Data);
                            break;
                        default:
                            break;
                    }                    
                    
                }
            }

            public double GetDistanceToTarget()
            {
                return distanceToTarget;
            }

            public int GetTargetsCount()
            {
                return targetscount;
            }

            public bool GetIsReadyToFire()
            {
                return isReadyToFire;
            }

            public string GetVectorString(Vector3D vector)
            {
                return $"{vector.X.ToString()}:{vector.Y.ToString()}:{vector.Z.ToString()}";
            }

            public string GetTargetsInfo()
            {
                return $"Целей:\n{targetscount}\nРасстояние до текущей:\n{distanceToTarget:F2} м\n\n{(isReadyToFire ? "Готов" : "Не готов")}";
            }
                        
        }















    }
}