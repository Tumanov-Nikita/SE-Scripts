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
using VRage.Game.VisualScripting;
using VRage.Scripting;
using VRageMath;
using static Sandbox.Game.World.MyWorldGenerator;
using static System.Net.WebRequestMethods;
using static System.Reflection.Metadata.BlobBuilder;

namespace AutoMinerVertHyd
{
    public sealed class Program : MyGridProgram

    {



        /*
         * Минимальная конфигурация дрона 
         * (Водородный, вертикальный, с батареями в качестве основной энергии):
         * Блок дистанционного управления х1
         * Программный блок х1
         * Коннектор х1
         * Полет ИИ х1
         * Регистратор событий ИИ x2:
         *  Регистратор событий ИИ К Базе:
         *   - Режим точности On; Выравнивание по гравитации Off
         *   -
         *   -
         *   - Таймер коннект Старт
         *  Регистратор событий ИИ От Базы:
         *   -
         *   - Полет ИИ Поведение ИИ Off
         * Таймер х3:
         *  Таймер К Базе:
         *   - Полет ИИ Поведение ИИ On; Регистратор ИИ К Базе Поведение ИИ On; Регистратор ИИ К Базе Воспроизвести
         *  Таймер коннект:
         *   - Коннектор запереть On; Баки Накопитель On; Батареи Зарядка Зарядка
         *  Таймер От Базы:
         *   - Полет ИИ Поведение ИИ On; Регистратор ИИ От Базы Поведение ИИ On; Регистратор ИИ От Базы Воспроизвести; Режим точности Off; Выравнивание по гравитации On
         */

        #region Настройки
        /// <summary>
        /// Количество шахт квадратно-гнездового метода в ширину
        /// </summary>
        public readonly byte ShaftM = 2;
        /// <summary>
        /// Количество шахт квадратно-гнездового метода в длину
        /// </summary>
        public readonly byte ShaftN = 2;
        /// <summary>
        /// Порог заполнения хранилищ, в %
        /// </summary>
        public readonly float StorageCapacityThreshold = 70;
        /// <summary>
        /// Порог заряда батарей, в %
        /// </summary>
        public readonly float BatteriesCapacityThreshold = 10;
        /// <summary>
        /// Порог заполнения водородных баков, в %
        /// </summary>
        public readonly float TanksCapacityThreshold = 50;
        /// <summary>
        /// Заправлять баки и заряжать батареи на базе до максимума
        /// </summary>
        public readonly bool FillUntillFull = true;
        /// <summary>
        /// Коэффициент для заполнения водородных баков и батарей, от 0 до 1
        /// </summary>
        public readonly float TanksAndBatteriesFillingCoeff = 0.75f;
        /// <summary>
        /// Максимальная высота над уровнем моря для дуги перемещения, в метрах
        /// </summary>
        public readonly float ArcHeightMaximum = 14000;
        /// <summary>
        /// Мультипликатор для сигнала гироскопам
        /// </summary>
        public readonly float GyroMult = 5;
        /// <summary>
        /// Ограничение скорости для перемещения на поверхности, в м/c
        /// </summary>
        public readonly float SurfaceSpeedLimit = 600;
        /// <summary>
        /// Ограничение скорости для перемещения в шахте, в м/c
        /// </summary>
        public readonly float MiningSpeedLimit = 0.15f;
        /// <summary>
        /// Максимально допустимая точность для совмещения с точкой назначения, в м
        /// </summary>
        public readonly float AcceptableMovingAccuracy = 0.5f;
        /// <summary>
        /// Дополнительный отступ от ширины/длины дрона для разметки шахт, в м
        /// </summary>
        public readonly float MiningMargin = 1.35f;
        /// <summary>
        /// Мультипликатор скорости для перемещения над поверхностью в режиме выкапывания шахты
        /// </summary>
        public readonly float AboveGroundSpeedMultiplier = 20f;
        /// <summary>
        /// Минимальная высота над поверхностью для ускоренного перемещения в режиме выкапывания шахты
        /// </summary>
        public readonly float AboveGroundSpeedHeight = 2f;
        /// <summary>
        /// Мультипликатор высоты для дуги перемещения, рекомендуется от 0.3 до 1
        /// </summary>
        public readonly float ArcHeightMult = 0.9f;

        #endregion

        #region Переменные для наименований блоков и групп блоков
        public readonly string EventControllerName = "Контроллер события ИИ Выгрузка";
        public readonly string FlightControllerName = "Полет ИИ Майнер";
        public readonly string RemoteControllerName = "ДУ ИИ Майнер";
        public readonly string TimerToBaseName = "Таймер к Базе для ИИ Майнер";
        public readonly string TimerFromBaseName = "Таймер от Базы для ИИ Майнер";
        public readonly string StoragesGroupName = "Контейнеры Майнер";
        public readonly string ConnectorGroupName = "Коннекторы Майнер";
        public readonly string GyroscopesGroupName = "Гироскопы Майнер";
        public readonly string ThrustersGroupName = "Двигатели Майнер";
        public readonly string BatteriesGroupName = "Батареи Майнер";
        public readonly string TanksGroupName = "Баки Майнер";
        public readonly string DrillsGroupName = "Буры Майнер";
        #endregion

        private char CurrentIcon;
        private string CurrentStatus = "";
        private static Program myScript;
        MiningHandler miningHandler;


        public Program()
        {
            myScript = this;
            miningHandler = new MiningHandler();

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
                case "mining":
                    CurrentStatus = "mining";
                    break;
                case "returnToBase":
                    CurrentStatus = "returnToBase";
                    break;
                case "parkingToBase":
                    CurrentStatus = "parkingToBase";
                    break;
                case "exitFromBase":
                    CurrentStatus = "exitFromBase";
                    break;
                case "movingToMine":
                    CurrentStatus = "movingToMine";
                    break;
                case "movingToCurrentShaft":
                    CurrentStatus = "movingToCurrentShaft";
                    break;
                default:
                    IconSpin();
                    myScript.Echo("Current status is " + CurrentStatus);
                    HandleStatus();
                    break;
            }
        }

        /// <summary>
        /// Обработка текущего статуса
        /// </summary>
        private void HandleStatus()
        {
            switch (CurrentStatus)
            {
                case "mining":
                    miningHandler.Mining();
                    break;
                case "returnToBase":
                    miningHandler.ReturningToBase();
                    break;
                case "parkingToBase":
                    miningHandler.ParkingToBase();
                    break;
                case "exitFromBase":
                    miningHandler.ExitFromBase();
                    break;
                case "movingToMine":
                    miningHandler.MovingToMine();
                    break;
                case "movingToCurrentShaft":
                    miningHandler.MovingToCurrentShaft();
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Косметическая крутилка
        /// </summary>
        private void IconSpin()
        {
            switch (CurrentIcon)
            {
                case '–':
                    CurrentIcon = '\\';
                    break;
                case '\\':
                    CurrentIcon = '/';
                    break;
                case '/':
                    CurrentIcon = '–';
                    break;
                default:
                    CurrentIcon = '–';
                    break;
            }
            myScript.Echo(CurrentIcon.ToString());
        }
        public class MiningHandler
        {
            #region Объявление переменных
            private readonly IMyFlightMovementBlock FlightMovement;
            private readonly IMyRemoteControl RemoteControl;
            private readonly IMyTimerBlock TimerForAIToBase, TimerForAIFromBase;
            private readonly List<IMyInventoryOwner> Storages;
            private readonly List<IMyBatteryBlock> Batteries;
            private readonly List<IMyGasTank> Tanks;
            private readonly List<IMyShipDrill> Drills;
            private readonly List<IMyGyro> Gyros;
            private readonly List<IMyShipConnector> Connectors;
            private readonly IMyBlockGroup StoragesGroup, BatteriesGroup, TanksGroup, DrillsGroup, ConnectorsGroup, GyroscopesGroup, ThrustersGroup;
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
            private readonly Vector3D SizeInMeters;
            private bool IsMiningComplete;
            private bool IsGridAlignedToGravity;
            private Vector3D ForwardVector;
            private Vector3D PlanetCenter;
            private Vector3D MineCenterPosition;
            private Vector3D CurrentMiningPosition;
            private Vector3D BasePosition;
            private List<ShaftMark> ShaftMarks = new List<ShaftMark>();
            private bool IsMovingIntoShaft;
            private bool StopForTurningAround;
            private Vector3D ArcStartPos;
            private Vector3D ArcTargetPos;
            private Vector3D ArcApexPos;
            private Vector3D ArcPlaneNormal;
            private bool ArcInitialized = false;
            private bool ArcIsAscending = true;

            #endregion

            public MiningHandler()
            {
                #region Начальная инициализация

                FlightMovement = (IMyFlightMovementBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.FlightControllerName);
                RemoteControl = (IMyRemoteControl)myScript.GridTerminalSystem.GetBlockWithName(myScript.RemoteControllerName);
                TimerForAIToBase = (IMyTimerBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.TimerToBaseName);
                TimerForAIFromBase = (IMyTimerBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.TimerFromBaseName);
                RemoteControl.ControlThrusters = true;
                MineCenterPosition = new Vector3D(0);
                CurrentMiningPosition = new Vector3D(0);
                BasePosition = new Vector3D(0);
                IsMiningComplete = false;
                Vector3I sizeInBlocks = RemoteControl.CubeGrid.Max - RemoteControl.CubeGrid.Min + new Vector3I(1, 1, 1);
                SizeInMeters = new Vector3D(sizeInBlocks) * RemoteControl.CubeGrid.GridSize;


                Gyros = new List<IMyGyro>();
                GyroscopesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.GyroscopesGroupName);
                GyroscopesGroup.GetBlocksOfType(Gyros);
                Connectors = new List<IMyShipConnector>();
                ConnectorsGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.ConnectorGroupName);
                ConnectorsGroup.GetBlocksOfType(Connectors);
                Storages = new List<IMyInventoryOwner>();
                StoragesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.StoragesGroupName);
                StoragesGroup.GetBlocksOfType(Storages);
                Batteries = new List<IMyBatteryBlock>();
                BatteriesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.BatteriesGroupName);
                BatteriesGroup.GetBlocksOfType(Batteries);
                Tanks = new List<IMyGasTank>();
                TanksGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.TanksGroupName);
                if (Tanks != null)
                {
                    TanksGroup.GetBlocksOfType(Tanks);
                }
                Drills = new List<IMyShipDrill>();
                DrillsGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.DrillsGroupName);
                DrillsGroup.GetBlocksOfType(Drills);

                //Инциализация двигателей по направлениям
                Matrix RemConMatrix = new Matrix();
                RemoteControl.Orientation.GetMatrix(out RemConMatrix);
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

                RemoteControl.TryGetPlanetPosition(out PlanetCenter);

                #endregion
            }

            #region Обработка статусов

            /// <summary>
            /// Режим выкапывания шахты
            /// </summary>
            public void Mining()
            {
                GravitationAligning();
                if (CheckStorageAndTanksAndBatteries())
                {
                    if (IsGridAlignedToGravity)
                    {

                        SetDrillsEnabled(true);

                        var currentValidShaft = GetCurrentShaft();
                        if (!currentValidShaft.endCoords.IsZero())
                        {
                            CurrentMiningPosition = currentValidShaft.endCoords;
                        }

                        var speedLimit = myScript.MiningSpeedLimit;
                        double elevationSurface;
                        RemoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationSurface);
                        if (elevationSurface - SizeInMeters.Y > myScript.AboveGroundSpeedHeight) // Увеличение скорости, если находимся больше, чем в AboveGroundSpeedHeight метрах над поверхностью
                        {
                            speedLimit *= myScript.AboveGroundSpeedMultiplier;
                        }

                        if (MovementOnVectorLinear(CurrentMiningPosition, speedLimit, true))
                        {
                            currentValidShaft.isFinished = true;
                            IsMovingIntoShaft = false;

                            SetDrillsEnabled(false);
                            SetGyrosOverride(false);
                            StopAllGyros();
                            StopAllThrusters();
                            SetStatus("movingToCurrentShaft");
                        }
                    }
                }
                else
                {
                    var currentValidShaft = GetCurrentShaft();
                    if (!currentValidShaft.startCoords.IsZero())
                    {
                        currentValidShaft.currDepthCoords = RemoteControl.GetPosition();
                    }
                    SetDrillsEnabled(false);
                    SetGyrosOverride(false);
                    StopAllGyros();
                    StopAllThrusters();
                    SetStatus("movingToCurrentShaft");
                }
            }
            /// <summary>
            /// Режим возврата на базу
            /// </summary>
            internal void ReturningToBase()
            {
                if (ArcInitialized && StopForTurningAround && RemoteControl.GetShipVelocities().LinearVelocity.Length() > myScript.MiningSpeedLimit)
                {
                    GravitationAligning();
                    StopAllThrusters();
                }
                else
                {
                    StopForTurningAround = false;

                    if (BasePosition.IsZero())
                    {
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                    }

                    if (MovementOnVectorArchwise(BasePosition, myScript.SurfaceSpeedLimit))
                    {
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        SetStatus("parkingToBase");
                    }
                }
            }
            /// <summary>
            /// Режим парковки на базе
            /// </summary>
            internal void ParkingToBase()
            {
                if (!FlightMovement.Enabled)
                {
                    FlightMovement.Enabled = true;
                    FlightMovement.AlignToPGravity = true;
                    SetConnectorsEnabled(true);
                    TimerForAIToBase.Trigger();
                }
                float tanksFillingThreshold = myScript.FillUntillFull ? 100 : CalculateFillingValue(myScript.TanksCapacityThreshold);
                float tanksChargingThreshold = myScript.FillUntillFull ? 100 : CalculateFillingValue(myScript.BatteriesCapacityThreshold);
                if (CheckStoragesInPercent() == 0
                    && CheckTanksInPercent() >= tanksFillingThreshold
                    && CheckBatteriesInPercent() >= tanksChargingThreshold
                    && FlightMovement.Enabled
                    && Connectors.Any(c => c.Status == MyShipConnectorStatus.Connected))
                {
                    FlightMovement.Enabled = false;
                    SetStatus("exitFromBase");
                }
            }
            /// <summary>
            /// Режим выхода с базы
            /// </summary>
            internal void ExitFromBase()
            {
                if (IsMiningComplete)
                {
                    SetStatus("Mining complete!");
                    myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                }
                else if (!FlightMovement.Enabled)
                {
                    SetBatteriesRecharge(false);
                    SetTanksStockpile(false);
                    SetConnectorsEnabled(false);
                    FlightMovement.Enabled = true;
                    FlightMovement.AlignToPGravity = true;
                    TimerForAIFromBase.Trigger();
                }

                if (!FlightMovement.IsAutoPilotEnabled)
                {
                    if (BasePosition.IsZero())
                    {
                        BasePosition = RemoteControl.GetPosition();
                    }
                    if (ForwardVector.IsZero())
                    {
                        ForwardVector = Vector3D.Normalize(Vector3D.Reject(
                            RemoteControl.WorldMatrix.Forward,
                            Vector3D.Normalize(RemoteControl.GetNaturalGravity())));
                    }
                    FlightMovement.Enabled = false;
                    SetConnectorsEnabled(true);
                    SetStatus("movingToMine");
                }
            }
            /// <summary>
            /// Режим перемещения к месту раскопок
            /// </summary>
            internal void MovingToMine()
            {
                if (CheckStorageAndTanksAndBatteries())
                {
                    var currentValidShaft = GetCurrentShaft();
                    if (currentValidShaft.startCoords.IsZero())
                    {
                        if (CurrentMiningPosition.IsZero())
                        {
                            if (MineCenterPosition.IsZero())
                            {
                                List<MyWaypointInfo> myWaypoints = new List<MyWaypointInfo>();
                                RemoteControl.GetWaypointInfo(myWaypoints);
                                if (myWaypoints.Count > 0)
                                {
                                    MineCenterPosition = myWaypoints[0].Coords;
                                }
                                else
                                {
                                    myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                                }
                            }
                            else
                            {
                                CurrentMiningPosition = MineCenterPosition;
                            }
                        }
                    }
                    else
                    {
                        CurrentMiningPosition = currentValidShaft.startCoords;
                    }

                    if (MovementOnVectorArchwise(CurrentMiningPosition, myScript.SurfaceSpeedLimit))
                    {
                        SetGyrosOverride(false);
                        IsMovingIntoShaft = true;
                        SetStatus("movingToCurrentShaft");
                    }
                }
                else
                {
                    StopForTurningAround = true;
                    SetGyrosOverride(false);
                    StopAllGyros();
                    StopAllThrusters();
                    SetStatus("returnToBase");
                }
            }
            /// <summary>
            /// Режим перемещения к старту текущей шахты
            /// </summary>
            internal void MovingToCurrentShaft()
            {
                GravitationAligning();
                if (CheckStorageAndTanksAndBatteries() && !IsMiningComplete)
                {
                    if (IsGridAlignedToGravity)
                    {
                        if (MineCenterPosition.IsZero())
                        {
                            List<MyWaypointInfo> myWaypoints = new List<MyWaypointInfo>();
                            RemoteControl.GetWaypointInfo(myWaypoints);
                            if (myWaypoints.Count > 0)
                            {
                                MineCenterPosition = myWaypoints[0].Coords;
                            }
                            else
                            {
                                MineCenterPosition = RemoteControl.GetPosition();
                            }
                        }

                        if (ShaftMarks.Count == 0)
                        {
                            if (ForwardVector.IsZero())
                            {
                                ForwardVector = Vector3D.Normalize(Vector3D.Reject(
                                    RemoteControl.WorldMatrix.Forward,
                                    Vector3D.Normalize(RemoteControl.GetNaturalGravity())));
                            }
                            CreateShaftMarks(ref ShaftMarks, MineCenterPosition, myScript.ShaftM, myScript.ShaftN);
                        }
                        var currentValidShaft = GetCurrentShaft();
                        if (!currentValidShaft.currDepthCoords.IsZero())
                        {
                            CurrentMiningPosition = currentValidShaft.currDepthCoords;
                        }
                        else
                        {
                            IsMiningComplete = true;
                        }
                        if (MovementOnVectorLinear(CurrentMiningPosition, myScript.MiningSpeedLimit * myScript.AboveGroundSpeedMultiplier, IsMovingIntoShaft))
                        {
                            IsMovingIntoShaft = true;
                            SetGyrosOverride(false);
                            StopAllGyros();
                            StopAllThrusters();
                            SetStatus("mining");
                        }
                    }
                }
                else
                {
                    var currentValidShaft = GetCurrentShaft();
                    if (!currentValidShaft.startCoords.IsZero())
                    {
                        CurrentMiningPosition = currentValidShaft.startCoords;
                    }
                    else
                    {
                        CurrentMiningPosition = MineCenterPosition;
                    }
                    if (MovementOnVectorLinear(CurrentMiningPosition, myScript.MiningSpeedLimit * myScript.AboveGroundSpeedMultiplier, false))
                    {
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        SetStatus("returnToBase");
                    }
                }
            }

            #endregion


            #region Методы для перемещения
            /// <summary>
            /// Выравнивает дрон по вектору планетарной гравитации, делает свой вектор Down сонаправленным ему
            /// </summary>
            private void GravitationAligning()
            {
                Vector3D gravVectorNorm = Vector3D.Normalize(RemoteControl.GetNaturalGravity());
                Vector3D axisGrav = gravVectorNorm.Cross(RemoteControl.WorldMatrix.Down);
                if (axisGrav.Dot(RemoteControl.WorldMatrix.Down) < 0)
                {
                    axisGrav = Vector3D.Normalize(axisGrav);
                }

                Vector3D currentForwardVector = Vector3D.Reject(ForwardVector, gravVectorNorm);
                Vector3D axisForward = currentForwardVector.Cross(RemoteControl.WorldMatrix.Forward);
                if (currentForwardVector.Dot(RemoteControl.WorldMatrix.Forward) < 0)
                {
                    axisForward = Vector3D.Normalize(axisForward);
                }

                float pitch = (float)axisGrav.Dot(RemoteControl.WorldMatrix.Right);
                float roll = (float)axisGrav.Dot(RemoteControl.WorldMatrix.Backward);
                float yaw = (float)axisForward.Dot(RemoteControl.WorldMatrix.Up);

                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.GyroOverride = true;
                    gyro.Pitch = pitch * myScript.GyroMult;
                    gyro.Roll = roll * myScript.GyroMult;
                    gyro.Yaw = yaw * myScript.GyroMult;
                }
                IsGridAlignedToGravity = axisGrav.Length() + axisForward.Length() < 0.01;
            }
            /// <summary>
            /// Устанавливает всем гироскопам значение перехвата управления
            /// </summary>
            /// <param name="overrideControls">Параметр, включающий или выключающий перехват управления</param>
            private void SetGyrosOverride(bool overrideControls)
            {
                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.GyroOverride = overrideControls;
                }
            }
            /// <summary>
            /// Устанавливает всем гироскопам значение 0 по тангажу, рысканию и крену
            /// </summary>
            private void StopAllGyros()
            {
                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.Pitch = 0;
                    gyro.Roll = 0;
                    gyro.Yaw = 0;
                }
            }
            /// <summary>
            /// Устанавливает процентное значение тяги двигателям
            /// </summary>
            /// <param name="list">Лист двигателей</param>
            /// <param name="value">Значение тяги (от 0 до 1)</param>
            private void SetTrustersPercentage(List<IMyThrust> list, float value)
            {
                foreach (IMyThrust thrust in list)
                {
                    thrust.ThrustOverridePercentage = value;
                }
            }
            /// <summary>
            /// Устанавливает значение тяги двигателям в Ньютонах
            /// </summary>
            /// <param name="list">Лист двигателей</param>
            /// <param name="value">Значение тяги</param>
            private void SetTrustersNewtons(List<IMyThrust> list, float value)
            {
                foreach (IMyThrust thrust in list)
                {
                    thrust.ThrustOverride = value / list.Count;
                }
            }
            /// <summary>
            /// Устанавливает значение тяги 0 всем двигателям
            /// </summary>
            private void StopAllThrusters()
            {
                SetTrustersPercentage(ThrForward, 0);
                SetTrustersPercentage(ThrBackward, 0);
                SetTrustersPercentage(ThrRight, 0);
                SetTrustersPercentage(ThrLeft, 0);
                SetTrustersPercentage(ThrUp, 0);
                SetTrustersPercentage(ThrDown, 0);
            }

            /// <summary>
            /// Перемещает дрон к конечной точке линейно по каждой из 3 осей
            /// </summary>
            /// <param name="target">Конечная точка</param>
            /// <param name="speedLimit">Ограничение скорости, в м/с</param>
            /// <param name="horizontalAligmentFirst">Сначала совмещение по горизонтальной плоскости (иначе - сначала совмещение по вертикальной оси)</param>
            /// <returns>true - если достиг конечной точки, false в остальных случаях</returns>
            private bool MovementOnVectorLinear(Vector3D target, float speedLimit, bool horizontalAligmentFirst)
            {

                RemoteControl.DampenersOverride = true;
                Vector3D linearVelocity = RemoteControl.GetShipVelocities().LinearVelocity;
                if (linearVelocity.Length() < speedLimit)
                {

                    Vector3D pathVector = target - RemoteControl.GetPosition();
                    Vector3D pathVectorForward = RemoteControl.WorldMatrix.Forward * pathVector.Dot(RemoteControl.WorldMatrix.Forward);
                    float ForwardScalar = (float)Vector3D.Normalize(pathVectorForward).Dot(RemoteControl.WorldMatrix.Forward);

                    Vector3D pathVectorRight = RemoteControl.WorldMatrix.Right * pathVector.Dot(RemoteControl.WorldMatrix.Right);
                    float RightScalar = (float)Vector3D.Normalize(pathVectorRight).Dot(RemoteControl.WorldMatrix.Right);

                    Vector3D pathVectorUp = RemoteControl.WorldMatrix.Up * pathVector.Dot(RemoteControl.WorldMatrix.Up);
                    float UpScalar = (float)Vector3D.Normalize(pathVectorUp).Dot(RemoteControl.WorldMatrix.Up);

                    if (linearVelocity.Length() < myScript.AcceptableMovingAccuracy / 2 && (pathVectorForward.Length() + pathVectorRight.Length() + pathVectorUp.Length()) / 3 < myScript.AcceptableMovingAccuracy)
                    {
                        StopAllThrusters();
                        return true;
                    }

                    float shipMass = RemoteControl.CalculateShipMass().PhysicalMass;

                    Vector3D velocityForward = RemoteControl.WorldMatrix.Forward * linearVelocity.Dot(RemoteControl.WorldMatrix.Forward);
                    Vector3D velocityRight = RemoteControl.WorldMatrix.Right * linearVelocity.Dot(RemoteControl.WorldMatrix.Right);
                    Vector3D velocityUp = RemoteControl.WorldMatrix.Up * linearVelocity.Dot(RemoteControl.WorldMatrix.Up);

                    float forwardVelScalar = (float)velocityForward.Dot(RemoteControl.WorldMatrix.Forward);
                    float stopDistForward = (float)(0.5 * shipMass * Math.Pow(forwardVelScalar, 2) / (forwardVelScalar > 0 ? BackwardThrustEff : ForwardThrustEff));
                    float rightVelScalar = (float)velocityRight.Dot(RemoteControl.WorldMatrix.Right);
                    float stopDistRight = (float)(0.5 * shipMass * Math.Pow(rightVelScalar, 2) / (rightVelScalar > 0 ? LeftThrustEff : RightThrustEff));
                    float upVelScalar = (float)velocityUp.Dot(RemoteControl.WorldMatrix.Up);
                    float stopDistUp = (float)(0.5 * shipMass * Math.Pow(upVelScalar, 2) / (upVelScalar > 0 ?
                                                                                                DownThrustEff + (shipMass * RemoteControl.GetNaturalGravity().Length()) :
                                                                                                UpThrustEff - (shipMass * RemoteControl.GetNaturalGravity().Length())));

                    //LCD.WriteText($"forwardSc = {ForwardScalar:F}\n", false);
                    //LCD.WriteText($"rightSc = {RightScalar:F}\n", true);
                    //LCD.WriteText($"upSc = {UpScalar:F}\n", true);
                    //LCD.WriteText($"stopDistForward = {stopDistForward:F}\n", true);
                    //LCD.WriteText($"stopDistRight = {stopDistRight:F}\n", true);
                    //LCD.WriteText($"stopDistUp = {stopDistUp:F}\n", true);

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
                            float keepElevationT = (float)(shipMass * RemoteControl.GetNaturalGravity().Length());
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
            /// <summary>
            /// Перемещает дрон к конечной точке через точку апекса
            /// </summary>
            /// <param name="target">Конечная точка</param>
            /// <param name="speedLimit">Ограничение скорости</param>
            /// <returns>true - если достиг конечной точки, false в остальных случаях</returns>
            private bool MovementOnVectorArchwise(Vector3D target, float speedLimit)
            {
                Vector3D currentPos = RemoteControl.GetPosition();
                if (!ArcInitialized || !ArcTargetPos.Equals(target))
                {
                    ArcStartPos = currentPos;
                    ArcTargetPos = target;
                    ArcIsAscending = true;
                    ArcInitialized = true;

                    Vector3D midPoint = (ArcStartPos + ArcTargetPos) / 2.0;
                    Vector3D planetUp = Vector3D.Normalize(midPoint - PlanetCenter);

                    double elevationAboveSeaLevel = 0;
                    RemoteControl.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out elevationAboveSeaLevel);

                    double currentDistFromCenter = (ArcStartPos - PlanetCenter).Length();
                    double seaLevelRadius = currentDistFromCenter - elevationAboveSeaLevel;
                    double calculatedArcHeight = elevationAboveSeaLevel + ((target - currentPos).Length() * myScript.ArcHeightMult) + Math.Abs(((target - currentPos).Dot(planetUp)) * 2);
                    double arcHeight = calculatedArcHeight > myScript.ArcHeightMaximum ? myScript.ArcHeightMaximum : calculatedArcHeight;
                    double apexRadius = seaLevelRadius + arcHeight;
                    ArcApexPos = PlanetCenter + planetUp * apexRadius;

                    Vector3D startToApex = ArcApexPos - ArcStartPos;
                    Vector3D startToTarget = ArcTargetPos - ArcStartPos;
                    ArcPlaneNormal = Vector3D.Normalize(Vector3D.Cross(startToApex, startToTarget));

                    if (ArcPlaneNormal.LengthSquared() < 0.001)
                    {
                        ArcPlaneNormal = Vector3D.Normalize(midPoint - PlanetCenter);
                    }
                }

                double distToTarget = Vector3D.Distance(currentPos, ArcTargetPos);
                Vector3D linearVelocity = RemoteControl.GetShipVelocities().LinearVelocity;

                if (distToTarget < myScript.AcceptableMovingAccuracy * 4 && linearVelocity.Length() <= 5)
                {
                    StopAllThrusters();
                    SetGyrosOverride(false);
                    StopAllGyros();
                    ArcInitialized = false;
                    ArcIsAscending = true;
                    return true;
                }
                Vector3D gravNorm = Vector3D.Normalize(RemoteControl.GetNaturalGravity());
                Vector3D rejTarget = Vector3D.Reject(target - currentPos, gravNorm);
                Vector3D rejApex = Vector3D.Reject(ArcApexPos - currentPos, gravNorm);
                ArcIsAscending = rejApex.Dot(Vector3D.Normalize(rejTarget)) > 0 && ArcIsAscending;

                OrientShipForArc();

                Vector3D currentTarget = ArcIsAscending ? ArcApexPos : ArcTargetPos;
                Vector3D toTarget = currentTarget - currentPos;
                double distToCurrentTarget = toTarget.Length();
                toTarget.Normalize();

                Vector3D shipUp = RemoteControl.WorldMatrix.Up;
                double speedAlongPath = Vector3D.Dot(linearVelocity, shipUp);
                double shipMass = RemoteControl.CalculateShipMass().PhysicalMass;
                double stopDist = 0;
                double availableThrust = 0;

                if (speedAlongPath > 0)
                {

                    availableThrust = DownThrustEff - (shipMass * (RemoteControl.GetNaturalGravity().Dot(Vector3D.Normalize(linearVelocity))));

                    stopDist = (0.5 * shipMass * speedAlongPath * speedAlongPath) / availableThrust;
                }
                else
                {
                    availableThrust = UpThrustEff - (shipMass * (RemoteControl.GetNaturalGravity().Dot(Vector3D.Normalize(linearVelocity))));
                    stopDist = ((0.5 * shipMass * speedAlongPath * speedAlongPath) / availableThrust) + (linearVelocity.Length() * 0.03);

                }
                bool shouldAccelerate = distToCurrentTarget > stopDist && Math.Abs(linearVelocity.Length()) < speedLimit;

                if (ArcIsAscending)
                {
                    if (shouldAccelerate && speedAlongPath < speedLimit)
                    {
                        SetTrustersPercentage(ThrUp, 1);
                        SetTrustersPercentage(ThrDown, 0);
                    }
                    else
                    {
                        if (speedAlongPath > 0)
                        {
                            if (DownThrustEff > 0)
                            {
                                SetTrustersPercentage(ThrDown, 1);
                                SetTrustersPercentage(ThrUp, 0);
                            }
                            else
                            {
                                StopAllThrusters();
                            }
                        }
                        else
                        {
                            StopAllThrusters();
                        }
                    }
                }
                else
                {
                    if (shouldAccelerate && Math.Abs(speedAlongPath) < speedLimit)
                    {
                        if (DownThrustEff > 0)
                        {
                            SetTrustersPercentage(ThrDown, 1);
                            SetTrustersPercentage(ThrUp, 0);
                        }
                        else
                        {
                            StopAllThrusters();
                        }
                    }
                    else
                    {
                        if (speedAlongPath < 0)
                        {
                            SetTrustersPercentage(ThrUp, 1);
                            SetTrustersPercentage(ThrDown, 0);
                        }
                        else
                        {
                            StopAllThrusters();
                        }
                    }
                }

                return false;
            }
            /// <summary>
            /// Ориентирет дрон, совмещая вектор Up с направлением к цели при подъеме, и вектор Down при снижении
            /// </summary>
            /// <returns>Возвращает true, если выравнивание завершено</returns>
            private bool OrientShipForArc()
            {
                Vector3D targetPoint = ArcIsAscending ? ArcApexPos : ArcTargetPos;
                Vector3D toTargetNorm = Vector3D.Normalize(targetPoint - RemoteControl.GetPosition());
                Vector3D axisTarget = toTargetNorm.Cross(RemoteControl.WorldMatrix.Up);
                if (!ArcIsAscending)
                {
                    axisTarget = -axisTarget;
                }
                Vector3D forwardTargetVector = Vector3D.Normalize(Vector3D.Reject(ArcTargetPos - RemoteControl.GetPosition(), ArcApexPos - RemoteControl.GetPosition()));
                Vector3D axisForward = forwardTargetVector.Cross(RemoteControl.WorldMatrix.Forward);
                if (forwardTargetVector.Dot(RemoteControl.WorldMatrix.Forward) < 0)
                {
                    axisForward = Vector3D.Normalize(axisForward);
                }

                float pitch = (float)axisTarget.Dot(RemoteControl.WorldMatrix.Right);
                float roll = (float)axisTarget.Dot(RemoteControl.WorldMatrix.Backward);
                float yaw = (float)axisForward.Dot(RemoteControl.WorldMatrix.Up);


                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.GyroOverride = true;
                    gyro.Pitch = pitch * myScript.GyroMult;
                    gyro.Roll = roll * myScript.GyroMult;
                    gyro.Yaw = yaw * myScript.GyroMult;
                }

                return pitch + roll + yaw < 0.001;
            }
            /// <summary>
            /// Устанавливает тягу группе двигателей определенной оси в зависимости от значения скаляра перемещения по этой оси
            /// </summary>
            /// <param name="thrPositive">Группа двигателей, толкающая дрон в положительном направлении по оси</param>
            /// <param name="thrNegative">Группа двигателей, толкающая дрон в отрицательном направлении по оси</param>
            /// <param name="scalar">Значение скаляра перемещения по оси</param>
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

            #endregion


            #region Прочие вспомогательные методы
            /// <summary>
            /// Проверяет заполненность хранилищ, водородных баков и заряда батарей
            /// </summary>
            /// <returns>true - если хранилища, водородные баки и батареи заполнены в пределах установленных пороговых значений</returns>
            private bool CheckStorageAndTanksAndBatteries()
            {
                return CheckStoragesInPercent() < myScript.StorageCapacityThreshold
                    && CheckTanksInPercent() > myScript.TanksCapacityThreshold
                    && CheckBatteriesInPercent() > myScript.BatteriesCapacityThreshold;
            }
            /// <summary>
            /// Возвращает текущую среднюю заполненность хранилищ
            /// </summary>
            /// <returns>Заполненность, в %</returns>
            private float CheckStoragesInPercent()
            {
                float maxFill = 0;
                float fill = 0;
                foreach (IMyInventoryOwner storage in Storages)
                {
                    fill += Convert.ToInt32(storage.GetInventory(0).CurrentVolume.RawValue);
                    maxFill += Convert.ToInt32(storage.GetInventory(0).MaxVolume.RawValue);
                }
                fill = 100 * fill / maxFill;
                return fill;
            }
            /// <summary>
            /// Возвращает текущую среднюю заполненность водородных баков
            /// </summary>
            /// <returns>Заполненность, в %</returns>
            private float CheckTanksInPercent()
            {
                if (Tanks.Count == 0)
                {
                    return 100;
                }
                float H2_O2Count = 0;
                double H2_O2Filled = 0;
                foreach (IMyGasTank gastank in Tanks)
                {
                    H2_O2Filled += gastank.FilledRatio * 100;
                    H2_O2Count++;
                }
                H2_O2Filled = H2_O2Filled / H2_O2Count;
                return (float)H2_O2Filled;
            }
            /// <summary>
            /// Возвращает текущий средний заряд батарей
            /// </summary>
            /// <returns>Заряд, в %</returns>
            private float CheckBatteriesInPercent()
            {
                float maxCharge = 0;
                float charge = 0;
                foreach (IMyBatteryBlock battery in Batteries)
                {
                    charge += battery.CurrentStoredPower;
                    maxCharge += battery.MaxStoredPower;
                }
                charge = 100 * charge / maxCharge;
                return charge;
            }
            /// <summary>
            /// Вычисляет значение, до которого нужно заполнить системы (баки или батареи)
            /// </summary>
            /// <param name="threshold">Пороговое значение для возврата на базу</param>
            /// <returns></returns>
            private float CalculateFillingValue(float threshold)
            {
                return threshold + ((100 - threshold) * myScript.TanksAndBatteriesFillingCoeff);
            }
            /// <summary>
            /// Устанавливает режим работы дрона
            /// </summary>
            /// <param name="status">Новый режим работы</param>
            private void SetStatus(string status)
            {
                myScript.CurrentStatus = status;
            }
            /// <summary>
            /// Устанавливает всем бурам дрона значение "Включено"
            /// </summary>
            /// <param name="enabled">Значение "Включено"</param>
            private void SetDrillsEnabled(bool enabled)
            {
                foreach (IMyShipDrill drill in Drills)
                {
                    drill.Enabled = enabled;
                }
            }
            /// <summary>
            /// Устанавливает всем коннекторам дрона значение "Включено"
            /// </summary>
            /// <param name="enabled">Значение "Включено"</param>
            private void SetConnectorsEnabled(bool enabled)
            {
                foreach (IMyShipConnector con in Connectors)
                {
                    con.Enabled = enabled;
                }
            }
            /// <summary>
            /// Устанавливает всем водородным бакам дрона значение "Накопитель"
            /// </summary>
            /// <param name="enabled">Значение "Накопитель"</param>
            private void SetTanksStockpile(bool enabled)
            {
                foreach (IMyGasTank gastank in Tanks)
                {
                    gastank.Stockpile = enabled;
                }
            }
            /// <summary>
            /// Устанавливает всем батареям дрона значение "Режим зарядки"
            /// </summary>
            /// <param name="recharge">Значение "Режим зарядки": true - Зарядка, false - Авто</param>
            private void SetBatteriesRecharge(bool recharge)
            {
                foreach (IMyBatteryBlock battery in Batteries)
                {
                    battery.ChargeMode = recharge ? ChargeMode.Recharge : ChargeMode.Auto;
                }
            }
            /// <summary>
            /// Создает разметку шахт
            /// </summary>
            /// <param name="shaftMarks">Лист меток шахт</param>
            /// <param name="initCoords">Стартовая точка</param>
            /// <param name="shaftM">Количество шахт в ширину</param>
            /// <param name="shaftN">Количество шахт в длину</param>
            private void CreateShaftMarks(ref List<ShaftMark> shaftMarks, Vector3D initCoords, byte shaftM, byte shaftN)
            {
                double elevationSurface;
                RemoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationSurface);

                double depthMult = (elevationSurface / (PlanetCenter - initCoords).Length()) * 2;


                if (shaftMarks.Count > 0)
                {
                    shaftMarks.Clear();
                }
                for (int i = 0; i < shaftM; i++)
                {
                    for (int j = 0; j < shaftN; j++)
                    {
                        ShaftMark shaftMark = new ShaftMark();
                        shaftMark.isFinished = false;
                        shaftMark.startCoords = initCoords + ((i - ((float)(shaftM - 1) / 2)) * (SizeInMeters.X + myScript.MiningMargin)) * RemoteControl.WorldMatrix.Right
                                                    + ((j - ((float)(shaftN - 1) / 2)) * (SizeInMeters.Z + myScript.MiningMargin)) * RemoteControl.WorldMatrix.Forward;
                        shaftMark.currDepthCoords = shaftMark.startCoords;
                        shaftMark.endCoords = ((PlanetCenter - shaftMark.startCoords) * depthMult) + shaftMark.startCoords;
                        shaftMarks.Add(shaftMark);
                    }
                }
            }
            /// <summary>
            /// Возвращает метку текущей незавершенной шахты
            /// </summary>
            private ShaftMark GetCurrentShaft()
            {
                foreach (var mark in ShaftMarks)
                {
                    if (!mark.isFinished)
                    {
                        return mark;
                    }
                }
                return new ShaftMark();
            }

            #endregion

            /// <summary>
            /// Метка шахты
            /// </summary>
            private class ShaftMark
            {
                /// <summary>
                /// Координаты начала шахты (верх)
                /// </summary>
                public Vector3D startCoords;
                /// <summary>
                /// Координаты текущей глубины шахты
                /// </summary>
                public Vector3D currDepthCoords;
                /// <summary>
                /// Координаты конца шахты (низ)
                /// </summary>
                public Vector3D endCoords;
                /// <summary>
                /// Шахта завершена
                /// </summary>
                public bool isFinished;
            }
        }















    }
}