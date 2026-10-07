using UnityEngine;

namespace LB
{
    /// <summary>
    /// Constantes de gameplay. Los valores imitan las proporciones de BombSquad
    /// (vida 1000, escudo 650, radio de explosion base y multiplicadores por tipo, etc.)
    /// reescaladas a nuestro tamano de personaje (~1.4 m).
    /// </summary>
    public static class Tuning
    {
        // Mundo
        public static readonly Vector3 Gravity = new Vector3(0f, -19f, 0f);
        public const float KillY = -7f;
        public const float ScreamHighY = 3f;  // por encima de esta altura (sobre la plataforma) tambien grita
        public const float ScreamY =-0.2f; // por debajo de la plataforma: empieza el grito de caida

        // Personaje
        // Sistema de porcentaje (estilo Smash Bros): el dano acumulado aumenta el empuje.
        public const float DamageToPercent = 0.02f;   // dano "bruto" -> % (bomba en el centro ~27%, golpe ~3-6%)
        public const float MaxPercent = 999f;
        public const float KnockBase = 0.3f;          // multiplicador de empuje a 0%
        public const float KnockPerPercent = 110f;    // +1.0 de multiplicador cada 110%
        public const float TumbleSpeed = 9f;          // a partir de este empuje se cae dando vueltas
        public const int MaxFieldDebris = 450;
        public const int MaxScorchMarks = 120;
        // Desmembramiento
        public const float LimbDamagePerBlast = 0.6f;   // dano a una extremidad por bomba a quemarropa (x3 si sus capas estan destrozadas)
        public const float ThrowPenaltyPerUnit = 0.22f; // distancia de lanzamiento perdida por mano (brazo = el doble)
        public const float DrawTimePerUnit = 0.3f;      // segundos extra para sacar la bomba por mano (brazo = el doble)
        public const float LimpMul = 0.65f;             // velocidad sin un pie
        public const float LimpBothMul = 0.5f;          // sin los dos pies
        public const float CrawlMul = 0.35f;            // a gatas (sin una pierna)
        public const float DragMul = 0.18f;             // arrastrandose (sin piernas)          // marcas de explosion en el suelo/barandas
        public const float SootPerBlast = 0.4f;         // hollin por bomba a quemarropa (1 = totalmente chamuscado)          // trozos que pueden quedarse en el campo a la vez
        public const float ChunkDamagePerBlast = 1.25f; // dano a cada trozo del cuerpo por bomba a quemarropa (resistencia ~1)
        public const int MaxChunkDebrisPerBlast = 40;   // trozos que salen volando por explosion y personaje
        public const float BlastKOPerPercent = 0.008f; // +0.8 s de aturdimiento por bomba cada 100%
        public const float MaxKOTime = 3.5f;
        public const float PunchKOBase = 0.45f;        // tiempo minimo en el suelo tras un golpe
        public const float RecoverHop = 5f;            // velocidad del brinquito al levantarse
        public const float RecoverTime = 0.35f;        // duracion del giro para quedar de pie
        public const float RecoverSpin = 620f;         // grados por segundo del giro
        public const float HealPercent = 50f;         // lo que quita el powerup de Salud
        public const float ThrowCharDamage = 300f;    // dano bruto al lanzar a otro jugador (~6%)
        public const float CharMass = 3f;
        public const float WalkSpeed = 3.8f;
        public const float RunSpeed = 6.2f;
        public const float GroundAccel = 38f;
        public const float AirAccel = 10f;
        public const float TurnRate = 13f;
        public const float JumpSpeed = 7.0f;
        public const float CapsuleRadius = 0.32f;
        public const float CapsuleHeight = 1.36f;
        public const float CenterHeight = 0.68f;

        // Estamina (0..100). Al llegar a 0 el personaje cae de cansancio.
        public const float StaminaMax = 100f;
        public const float JumpCost = 10f;
        public const float RunCostPerSec = 50f;        // 2 s de sprint con estamina llena
        public const float HoldMoveCostPerSec = 50f;   // moverse con alguien agarrado
        public const float ThrowCostMax = 50f;         // lanzar a la distancia maxima
        public const float PunchCost = 8f;
        public const float StaminaRegenPerSec = 30f;
        public const float StaminaRegenDelay = 0.6f;   // espera tras gastar antes de recuperar
        public const float ExhaustMin = 0.5f;          // tiempo en el suelo por cansancio a 0%
        public const float ExhaustMax = 2f;            // ... y a ExhaustFullPercent o mas
        public const float ExhaustFullPercent = 200f;

        // Golpes
        public const float PunchDuration = 0.3f;
        public const float PunchHitDelay = 0.09f;
        public const float PunchCooldown = 0.33f;
        public const float PunchBase = 140f;
        public const float PunchSpeedBonus = 26f;
        public const float GlovesMul = 2.1f;
        public const float PunchKnock = 5.5f;
        public const float PunchKOThreshold = 300f;

        // Lanzar / agarrar: mantener el boton carga la fuerza (de ThrowMin a ThrowMax en ThrowChargeTime s).
        public const float ThrowChargeTime = 1.0f;
        public const float ThrowMinSpeed = 2.2f;
        public const float ThrowMaxSpeed = 10f;
        public const float ThrowUpMin = 4.2f;
        public const float ThrowUpMax = 7.2f;
        public const float ThrowHeight = 1.0f;
        public const float ThrowCharacterMul = 0.75f;
        // Soltarse de un agarre: con mas % hacen falta mas pulsaciones y te retienen mas tiempo.
        public const float StruggleBase = 3f;
        public const float StrugglePerPercent = 25f;  // +1 pulsacion cada 25%
        public const float MaxHoldBase = 1.6f;
        public const float MaxHoldPerPercent = 60f;   // +1 s cada 60%
        public const float MaxHoldCap = 5f;
        public const float BehindEscapeChance = 0.2f;        // agarrado por la espalda: probabilidad por golpe a 0%
        public const float BehindEscapePercentScale = 100f;  // a 100% la probabilidad se reduce a la mitad
        public const float StruggleJolt = 1.6f;               // empujon que recibe el agarrador con cada forcejeo
        public const float HoldSpeedMul = 0.5f;              // velocidad cargando a alguien (un solo agarrador)
        public const float CoHoldMaxDist = 2.6f;             // si los dos agarradores se separan mas, se suelta el segundo

        // Bombas (radio base 2.0 en BombSquad, reescalado)
        public const float FuseTime = 2.6f;
        public const float BlastRadius = 2.5f;
        public const float BlastDamage = 1350f;
        public const float BlastKnock = 8f;
        public const float BlastUp = 6f;
        public const float LandMineArmTime = 1.0f;

        public static float RadiusMul(BombType t)
        {
            switch (t)
            {
                case BombType.Ice: return 1.2f;
                case BombType.Impact: return 0.7f;
                case BombType.LandMine: return 0.7f;
                default: return 1f;
            }
        }

        public const float TntRadiusMul = 1.45f;
        public const float TntRespawn = 20f;

        // Powerups
        public const float PowerupWearOff = 20f;
        public const float ShieldHp = 650f;
        public const float CurseTime = 5f;
        public const float PowerupLife = 20f;
        public const float PowerupInterval = 8f;
        public const int MaxPowerups = 6;
        public const float FreezeTime = 5f;

        // Partida
        public const float RespawnDelay = 2.5f;
        public const float KillCreditTime = 8f;
        public const int DefaultKillsToWin = 5;
    }
}
