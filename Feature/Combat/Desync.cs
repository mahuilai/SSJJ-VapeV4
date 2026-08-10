using Assets.Sources.Components.Interface.Info.Weapon;
using Assets.Sources.Utils.Player;
using Assets.Sources.Utils.Weapon;
using data;
using physics;
using Vape.Cfg;
using Vape.Entity;
using SSJJUserCmd;
using System.Linq;
using UnityEngine;

public static class Desync
{
    private const float DegreesToRadians = Mathf.PI / 180f;
    private static float WeaponSpread;
    private static bool _wasDesyncEnabled;
    private static float _lastRandomizeAt;
    private static float _lastYawHopAt;
    private static float _jitterOffset;
    private static bool _microMoveFlip;

    public static float SharedYaw { get; private set; }
    public static float SharedPitch { get; private set; }
    public static bool IsSilentAiming { get; private set; }
    public static void SetPitchAngle(ref float pitch)
    {
        pitch = Config.DesyncPitch;
    }

    public static void SetYawAngle()
    {
        if (Input.GetKeyDown(KeyCode.Z)) Config.DesyncYaw = -90f;
        if (Input.GetKeyDown(KeyCode.X)) Config.DesyncYaw = -180f;
        if (Input.GetKeyDown(KeyCode.C)) Config.DesyncYaw = 90f;
    }

    public static void ExecuteDesync(ref float pitch, UserCmd userCmd, ref float _pitch, ref float _yaw, ref float _moveforward, ref float _moveright, ref int _buttons, ref bool _silenting)
    {
        if (PlayerUpdate.LocalEntity == null ||
            PlayerUpdate.LocalEntity._entity == null ||
            PlayerUpdate.EntityList == null ||
            Contexts.sharedInstance == null ||
            Contexts.sharedInstance.weapon == null)
        {
            _pitch = userCmd.CameraPitch / 100f;
            _yaw = userCmd.CameraYaw / 100f;
            _moveforward = userCmd.MoveForward;
            _moveright = userCmd.MoveRight;
            _buttons = userCmd.Buttons;
            _silenting = false;
            return;
        }

        bool desyncEnabled = Config.Desync;
        if (desyncEnabled && !_wasDesyncEnabled)
        {
            ResetAuraSilentState();
            _wasDesyncEnabled = true;
        }
        else if (!desyncEnabled && _wasDesyncEnabled)
        {
            ResetAuraSilentState();
            _wasDesyncEnabled = false;
        }

        SetYawAngle();

        float cameraYaw = userCmd.CameraYaw / 100f;
        float cameraPitch = userCmd.CameraPitch / 100f;
        float outputYaw = cameraYaw;
        float outputPitch = pitch != 0f ? pitch : cameraPitch;
        float moveforward = userCmd.MoveForward;
        float moveright = userCmd.MoveRight;
        float originalForward = moveforward;
        float originalRight = moveright;
        int buttons = userCmd.Buttons;

        if (desyncEnabled)
        {
            UpdateAuraRandomState();
            SetPitchAngle(ref outputPitch);

            float targetYaw = cameraYaw + Config.DesyncYaw;
            switch (Config.DesyncMode)
            {
                case 1:
                    targetYaw = cameraYaw + 180f + (userCmd.Seq * Config.DesyncSpin % 360);
                    break;

                case 2:
                    targetYaw = cameraYaw + Config.DesyncYaw + _jitterOffset;
                    break;
            }

            if (Time.unscaledTime - _lastYawHopAt > 0.22f)
            {
                _lastYawHopAt = Time.unscaledTime;
                targetYaw += 120f;
            }

            outputYaw = NormalizeAngle(targetYaw);

            if (Mathf.Abs(moveforward) < 1f && Mathf.Abs(moveright) < 1f)
            {
                _microMoveFlip = !_microMoveFlip;
                moveforward = _microMoveFlip ? 0.0001f : -0.0001f;
                moveright = _microMoveFlip ? 0.0001f : -0.0001f;
            }
        }

        bool CanShoot = false;
        bool isweaponnotnull = Contexts.sharedInstance.weapon.currentWeaponEntity != null;

        WeaponSpread = CalculateWeaponSpread(userCmd);

        if (isweaponnotnull)
        {
            bool _canshoot;
            if (WeaponUtility.CanAttack(Contexts.sharedInstance.weapon.currentWeaponEntity, PlayerUpdate.LocalEntity.CilentTime + userCmd.FrameInterval))
            {
                _canshoot = WeaponSpread >= (Config.Accurary / 100f);
            }
            else
            {
                _canshoot = false;
            }
            CanShoot = _canshoot;
        }

        bool silenting = false;
        bool canSilentAim;
        if (CanShoot)
        {
            canSilentAim = HardAim.RunHardAim(PlayerUpdate.EntityList, PlayerUpdate.LocalEntity, ref outputYaw, ref outputPitch);
        }
        else
        {
            canSilentAim = false;
        }

        if (canSilentAim)
        {
            var currentWeapon = Contexts.sharedInstance.weapon.currentWeaponEntity;
            if (currentWeapon != null && currentWeapon.hasClip && currentWeapon.clip != null)
            {
                if (!userCmd.IsAttackOn)
                {
                    if (currentWeapon.clip.Clip > 0)
                    {
                        userCmd.Buttons |= 64;
                        buttons = buttons | 64;
                    }
                    if (currentWeapon.clip.Clip2 > 0)
                    {
                        userCmd.Buttons |= 512;
                        buttons = buttons | 512;
                    }
                }
            }

            silenting = true;
        }

        FixMove(outputYaw, cameraYaw, ref moveforward, ref moveright);

        bool keepLegit = !silenting && CanShoot && (userCmd.IsAttackOn || userCmd.IsSecondaryAttackOn);
        if (PlayerUpdate.LocalEntity?._entity?.currentWeapon != null &&
            PlayerUpdate.LocalEntity._entity.currentWeapon.Weapon == 4)
        {
            keepLegit = true;
        }

        if (keepLegit)
        {
            outputYaw = cameraYaw;
            outputPitch = cameraPitch;
            moveforward = originalForward;
            moveright = originalRight;
        }

        SharedYaw = outputYaw;
        SharedPitch = outputPitch;
        _pitch = outputPitch;
        _yaw = outputYaw;
        _buttons = buttons;
        _moveforward = moveforward;
        _moveright = moveright;
        _silenting = silenting;
        IsSilentAiming = silenting;
    }

    private static void ResetAuraSilentState()
    {
        _lastRandomizeAt = 0f;
        _lastYawHopAt = 0f;
        _jitterOffset = 0f;
        _microMoveFlip = false;
    }

    private static void UpdateAuraRandomState()
    {
        float now = Time.unscaledTime;
        if (now - _lastRandomizeAt <= 0.05f)
            return;

        _lastRandomizeAt = now;
        _jitterOffset = Random.Range(Config.DesyncJitterMin, Config.DesyncJitterMax);
    }

    private static float NormalizeAngle(float angle)
    {
        if (float.IsNaN(angle) || float.IsInfinity(angle))
            return 0f;

        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;
        return angle;
    }

    private static void FixMove(
        float targetYaw,
        float originalYaw,
        ref float forwardMove,
        ref float rightMove)
    {
        float normalizedOriginalYaw = originalYaw >= 0f ? originalYaw : originalYaw + 360f;
        float normalizedTargetYaw = targetYaw >= 0f ? targetYaw : targetYaw + 360f;

        float angleDifference = CalculateAngleDifference(normalizedTargetYaw, normalizedOriginalYaw);
        float correctedAngle = 360f - angleDifference;

        float originalForward = forwardMove;
        float originalRight = rightMove;

        float cosAngle = Mathf.Cos(correctedAngle * DegreesToRadians);
        float sinAngle = Mathf.Sin(correctedAngle * DegreesToRadians);
        float cosAngle90 = Mathf.Cos((correctedAngle + 90f) * DegreesToRadians);
        float sinAngle90 = Mathf.Sin((correctedAngle + 90f) * DegreesToRadians);

        forwardMove = cosAngle * originalForward + cosAngle90 * originalRight;
        rightMove = sinAngle * originalForward + sinAngle90 * originalRight;

        forwardMove = Mathf.Clamp(forwardMove, -100f, 100f);
        rightMove = Mathf.Clamp(rightMove, -100f, 100f);
    }

    private static float CalculateAngleDifference(float angle1, float angle2)
    {
        return angle1 >= angle2
            ? 360f - Mathf.Abs(angle1 - angle2)
            : Mathf.Abs(angle1 - angle2);
    }
    public static float CalculateWeaponSpread(UserCmd userCommand)
    {
        bool isContextInvalid =
            Contexts.sharedInstance == null ||
            Contexts.sharedInstance.weapon == null ||
            Contexts.sharedInstance.battleRoom == null ||
            Contexts.sharedInstance.weapon.currentWeaponEntity == null ||
            Contexts.sharedInstance.player == null;

        if (isContextInvalid)
            return 0f;

        var weaponEntity = Contexts.sharedInstance.weapon.currentWeaponEntity;
        if (weaponEntity.basicInfo == null || weaponEntity.basicInfo.Info == null)
            return 0f;

        if (!weaponEntity.hasSpread || weaponEntity.spread == null ||
            !weaponEntity.hasAccuracy || weaponEntity.accuracy == null)
            return 0f;

        if (PlayerUpdate.LocalEntity == null || PlayerUpdate.LocalEntity._entity == null)
            return 0f;

        IEntitsWeaponInfo weaponInfo = weaponEntity.basicInfo.Info;

        if (Contexts.sharedInstance.battleRoom.pyEngine == null)
            return 0f;

        IPyEngine physicsEngine = Contexts.sharedInstance.battleRoom.pyEngine.PyEngine;
        PlayerEntity player = PlayerUpdate.LocalEntity._entity;
        WeaponEntity currentWeapon = Contexts.sharedInstance.weapon.currentWeaponEntity;

        if (physicsEngine == null)
            return 0f;

        if (!player.hasClientTime || player.clientTime == null)
            return 0f;

        bool isWeightless = (physicsEngine.GetWorld().GetSceneMoveData() as SceneMoveData)?.isWeightlessness ?? false;

        bool shouldProcessSpread =
            !userCommand.PredicatedOnce &&
            weaponInfo.AccuracyLogic != null &&
            weaponInfo.SpreadLogic != null;

        if (shouldProcessSpread)
        {
            weaponInfo.SpreadLogic.BeforeFire(out currentWeapon.spread.Spread, player, currentWeapon, userCommand, isWeightless);
            weaponInfo.AccuracyLogic.BeforeFire(userCommand.Seq, player, currentWeapon, player.clientTime.ClientTime);
        }

        float baseSpread;
        float spreadModifier = currentWeapon.spread.Spread;

        switch (weaponInfo.WeaponType)
        {
            case 0:
                baseSpread = currentWeapon.accuracy.Accuracy * 100f / 92f;
                break;

            case 1:
            case 6:
            case 14:
                baseSpread = 1f - (currentWeapon.accuracy.Accuracy - weaponInfo.DefaultAccuracy) * 100f
                             / ((weaponInfo.MaxInaccuracy - weaponInfo.DefaultAccuracy) * 100f);
                spreadModifier = currentWeapon.spread.Spread;
                break;

            case 5:
                baseSpread = 1f;
                float playerSpeed = PlayerUtility.PlayerLength2D(player);
                spreadModifier = playerSpeed > 350f ? 0.4f : (playerSpeed > 25f ? 0.7f : 0f);
                break;

            case 10:
            case 12:
                baseSpread = 1f - (currentWeapon.accuracy.Accuracy - weaponInfo.AccuracyOffset) * 100f
                             / ((weaponInfo.MaxInaccuracy - weaponInfo.AccuracyOffset) * 100f);
                spreadModifier = currentWeapon.spread.Spread;
                break;

            default:
                baseSpread = 0f;
                spreadModifier = currentWeapon.spread.Spread;
                break;
        }

        float spreadDelta = Mathf.Clamp(baseSpread - spreadModifier, 0f, 1f);
        return spreadDelta;
    }
}
