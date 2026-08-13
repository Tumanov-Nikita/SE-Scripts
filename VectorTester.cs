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
using static VRageMath.Base6Directions;

namespace VectorTester
{
    public sealed class Program : MyGridProgram

    {

















        public readonly float GyroMult = 1;
        public readonly float SurfaceSpeedLimit = 600;
        public readonly float AcceptableMovingAccuracy = 0.5f;
        public readonly float AboveGroundSpeedMultiplier = 20f;
        public readonly float ArcHeightMult = 1.0f;
        public readonly float ArcHeightMaximum = 14000;




        #region Переменные для наименований блоков и групп блоков
        public readonly string RemoteControllerName = "ДУ ИИ Тестер";
        //public readonly string GyroscopesGroupName = "Гироскопы Тестер";
        //public readonly string ThrustersGroupName = "Двигатели Тестер";
        public readonly string DisplayName = "Дисплей Тестер";
        #endregion

        private char CurrentIcon;
        private string CurrentStatus = "";
        private static Program myScript;
        TestingHandler testingHandler;


        public Program()
        {
            myScript = this;
            testingHandler = new TestingHandler();

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
                case "orient":
                    CurrentStatus = "orient";
                    break;
                case "changeStage":
                    testingHandler.ArcIsAscending = !testingHandler.ArcIsAscending;
                    break;
                default:
                    IconSpin();
                    myScript.Echo("Current status is " + CurrentStatus);
                    myScript.Echo("ArcIsAscending is " + testingHandler.ArcIsAscending);
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
                case "orient":
                    testingHandler.Orient();
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

        public class TestingHandler
        {
            #region Объявление переменных
            private readonly IMyRemoteControl RemoteControl;
            private readonly List<IMyGyro> Gyros;
            //private readonly IMyBlockGroup GyroscopesGroup, ThrustersGroup;
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
            private bool IsGridAlignedToGravity;
            private Vector3D PlanetCenter;
            private Vector3D ArcStartPos;
            private Vector3D ArcTargetPos;
            private Vector3D ArcApexPos;
            private Vector3D ArcPlaneNormal;
            private Vector3D Target;
            private bool ArcInitialized = false;
            public bool ArcIsAscending = true;

            IMyTextPanel display;

            #endregion

            public TestingHandler()
            {
                #region Начальная инициализация

                RemoteControl = (IMyRemoteControl)myScript.GridTerminalSystem.GetBlockWithName(myScript.RemoteControllerName);
                RemoteControl.ControlThrusters = true;

                Target = new Vector3D(0);


                Gyros = new List<IMyGyro>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyGyro>(Gyros, (a) => (a.IsSameConstructAs(RemoteControl)));

                //Инциализация двигателей по направлениям
                Matrix RemConMatrix = new Matrix();
                RemoteControl.Orientation.GetMatrix(out RemConMatrix);
                Matrix ThrMatrix = new Matrix();
                List<IMyThrust> ThrTemp = new List<IMyThrust>();
                myScript.GridTerminalSystem.GetBlocksOfType<IMyThrust>(ThrTemp, (a) => (a.IsSameConstructAs(RemoteControl)));

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


                display = (IMyTextPanel)myScript.GridTerminalSystem.GetBlockWithName(myScript.DisplayName);

                #endregion
            }

            #region Обработка статусов

            public void Orient()
            {
                Vector3D currentPos = RemoteControl.GetPosition();
                if (Target.IsZero())
                {
                    List<MyWaypointInfo> myWaypoints = new List<MyWaypointInfo>();
                    RemoteControl.GetWaypointInfo(myWaypoints);
                    if (myWaypoints.Count > 0)
                    {
                        Target = myWaypoints[0].Coords;
                    }
                    else
                    {
                        myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                    }
                }
                if (!ArcInitialized || !ArcTargetPos.Equals(Target))
                {
                    ArcStartPos = currentPos;
                    ArcTargetPos = Target;
                    ArcIsAscending = true;
                    ArcInitialized = true;

                    Vector3D midPoint = (ArcStartPos + ArcTargetPos) / 2.0;
                    Vector3D planetUp = Vector3D.Normalize(midPoint - PlanetCenter);

                    double elevationAboveSeaLevel = 0;
                    RemoteControl.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out elevationAboveSeaLevel);

                    double currentDistFromCenter = (ArcStartPos - PlanetCenter).Length();
                    double seaLevelRadius = currentDistFromCenter - elevationAboveSeaLevel;
                    double calculatedArcHeight = elevationAboveSeaLevel + ((Target - currentPos).Length() * myScript.ArcHeightMult) + Math.Abs(((Target - currentPos).Dot(planetUp)) * 2);
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
                PrintVector(Target, "Target", false);
                PrintVector(ArcApexPos, "Apex", true);
                display.WriteText($"ArcIsAscending = {ArcIsAscending}\n", true);

                Vector3D gravNorm = Vector3D.Normalize(RemoteControl.GetNaturalGravity());
                Vector3D rejTarget = Vector3D.Reject(Target - currentPos, gravNorm);
                Vector3D rejApex = Vector3D.Reject(ArcApexPos - currentPos, gravNorm);
                ArcIsAscending = rejApex.Dot(Vector3D.Normalize(rejTarget)) > 0 && ArcIsAscending;



                //KeepOrientation(ArcTargetPos - RemoteControl.GetPosition());
                OrientShipForArc();

                //if (MovementOnVectorArchwise(Target, myScript.SurfaceSpeedLimit))
                //{
                //    SetGyrosOverride(false);
                //    StopAllGyros();
                //    StopAllThrusters();
                //    display.WriteText("Target was reached!\n", true);
                //    myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                //}

            }

            #endregion


            #region Методы для перемещения
            
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

                    if (pathVectorForward.Length() > stopDistForward && pathVectorForward.Length() > myScript.AcceptableMovingAccuracy //движение по продольной оси
                        && (horizontalAligmentFirst || pathVectorUp.Length() < myScript.AcceptableMovingAccuracy))
                    {
                        SetAxisThrustsByScalar(ThrForward, ThrBackward, ForwardScalar);
                    }
                    else
                    {
                        SetTrustersPercentage(ThrForward, 0);
                        SetTrustersPercentage(ThrBackward, 0);
                    }

                    if (pathVectorRight.Length() > stopDistRight && pathVectorRight.Length() > myScript.AcceptableMovingAccuracy //движение по поперечной оси
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
                        if (UpScalar > 0) //подъем
                        {
                            SetTrustersPercentage(ThrUp, 1);
                        }
                        else //снижение
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

                PrintVector(ArcApexPos, "Apex", false);
                PrintVector(ArcTargetPos, "Target", true);
                display.WriteText($"ArcIsAscending = {ArcIsAscending}\n", true);
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
                Vector3D headingVector = ArcIsAscending ? RemoteControl.WorldMatrix.Up : RemoteControl.WorldMatrix.Down;
                Vector3D currentPos = RemoteControl.GetPosition();
                Vector3D toTargetNorm = Vector3D.Normalize((ArcIsAscending ? ArcApexPos : ArcTargetPos) - currentPos);
                Vector3D axisTarget = toTargetNorm.Cross(headingVector);
                if (toTargetNorm.Dot(headingVector) < 0)
                {
                    axisTarget = Vector3D.Normalize(axisTarget);
                }

                Vector3D forwardTargetVector;

                if (ArcIsAscending)
                {
                    forwardTargetVector = Vector3D.Normalize(
                    Vector3D.Reject(
                        Vector3D.Normalize(ArcTargetPos - ArcStartPos),
                        Vector3D.Normalize(ArcApexPos - ArcStartPos)));
                }
                else
                {
                    forwardTargetVector = Vector3D.Zero;
                }
                

                //PrintVector(forwardTargetVector + ArcApexPos, "DescendingVector", true);
                //PrintVector(forwardTargetVector + currentPos, "forwardVector", true);
                Vector3D axisForward = forwardTargetVector.Cross(RemoteControl.WorldMatrix.Forward);
                if (forwardTargetVector.Dot(RemoteControl.WorldMatrix.Forward) < 0)
                {
                    axisForward = Vector3D.Normalize(axisForward);
                }

                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.GyroOverride = true;

                    gyro.Yaw = (float)(axisTarget + axisForward).Dot(gyro.WorldMatrix.Up) * myScript.GyroMult;
                    gyro.Pitch = (float)(axisTarget + axisForward).Dot(gyro.WorldMatrix.Right) * myScript.GyroMult;
                    gyro.Roll = (float)(axisTarget + axisForward).Dot(gyro.WorldMatrix.Backward) * myScript.GyroMult;
                }

                return axisTarget.Length() + axisForward.Length() < 0.01;
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



            public void KeepOrientation(Vector3D target)
            {
                Vector3D headingVector = ArcIsAscending ? RemoteControl.WorldMatrix.Up : RemoteControl.WorldMatrix.Down;

                //PrintVector(target, "target", true);
                Vector3D targetNorm = Vector3D.Normalize(target);
                //PrintVector(targetNorm + RemoteControl.GetPosition(), "Normalized target", true);
                Vector3D axisTarget = targetNorm.Cross(headingVector);
                if (targetNorm.Dot(headingVector) < 0)
                {
                    axisTarget = Vector3D.Normalize(axisTarget);
                }
                //PrintVector(axisTarget + RemoteControl.GetPosition(), "Rolling axis", true);
                //SetGyroOnVector(axisTarget);

                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.Yaw = (float)axisTarget.Dot(gyro.WorldMatrix.Up) * myScript.GyroMult;
                    gyro.Pitch = (float)axisTarget.Dot(gyro.WorldMatrix.Right) * myScript.GyroMult;
                    gyro.Roll = (float)axisTarget.Dot(gyro.WorldMatrix.Backward) * myScript.GyroMult;
                }
            }

            public void SetGyroOnVector(Vector3D axis)
            {
                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.Yaw = (float)axis.Dot(gyro.WorldMatrix.Up) * myScript.GyroMult;
                    gyro.Pitch = (float)axis.Dot(gyro.WorldMatrix.Right) * myScript.GyroMult;
                    gyro.Roll = (float)axis.Dot(gyro.WorldMatrix.Backward) * myScript.GyroMult;
                }
            }


            #endregion


            #region Прочие вспомогательные методы

            private void SetStatus(string status)
            {
                myScript.CurrentStatus = status;
            }


            // Testing
            private void PrintVector(Vector3D vector, string name, bool append, string colorHEX = "#FF00FF")
            {
                display.WriteText($"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n", append);
            }
            // Testing

            #endregion
        }























    }
}