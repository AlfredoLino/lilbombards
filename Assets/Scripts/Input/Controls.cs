using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LB
{
    /// <summary>Estado de controles de un jugador en un frame (botones = mantenidos).</summary>
    public struct InputState
    {
        public Vector2 move;
        public bool jump, punch, bomb, pickup, run;

        /// <summary>Modo raton: 1er clic saca la bomba, 2o clic (mantener) carga y al soltar lanza hacia el puntero.</summary>
        public bool clickBomb;
        public bool hasAim;
        public Vector3 aimPoint;

        public bool AnyAction => jump || punch || bomb || pickup;
    }

    public interface IInputProvider
    {
        InputState Read();
    }

    /// <summary>Teclas que usa el juego, mapeadas al Input System nuevo o al Input Manager clasico.</summary>
    public enum K
    {
        W, A, S, D, Space, J, Kk, L, I, LeftShift,
        Up, Down, Left, Right, Np0, Np1, Np2, Np3, Comma, Period, Slash, RightCtrl, RightShift,
        Enter, NpEnter, Escape, Minus, Equals, NpPlus, NpMinus, Tab, F1
    }

    public static class Keys
    {
        /// <summary>Teclado ocupado escribiendo en un campo de texto del menu online.</summary>
        public static bool Blocked;
        /// <summary>Raton sobre el panel del menu online (los clics no cuentan para el juego).</summary>
        public static bool MouseOverUI;

        /// <summary>Boton del raton: 0 = izquierdo, 1 = derecho, 2 = central (rueda).</summary>
        public static bool MouseHeld(int button)
        {
            if (MouseOverUI) return false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            var b = button == 0 ? m.leftButton : (button == 1 ? m.rightButton : m.middleButton);
            return b.isPressed;
#else
            return Input.GetMouseButton(button);
#endif
        }

        public static bool MouseDown(int button)
        {
            if (MouseOverUI) return false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            var b = button == 0 ? m.leftButton : (button == 1 ? m.rightButton : m.middleButton);
            return b.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        public static bool MouseLeftHeld() => MouseHeld(0);
        public static bool MouseLeftDown() => MouseDown(0);

        /// <summary>Punto del suelo (plano y=0) bajo el puntero del raton.</summary>
        public static bool MouseAim(out Vector3 point)
        {
            point = Vector3.zero;
            if (CameraRig.I == null) return false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            Vector2 sp = m.position.ReadValue();
#else
            Vector2 sp = Input.mousePosition;
#endif
            Ray ray = CameraRig.I.Cam.ScreenPointToRay(new Vector3(sp.x, sp.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        public static bool Held(K k)
        {
            if (Blocked) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].isPressed;
#else
            return Input.GetKey(Map(k));
#endif
        }

        public static bool Down(K k)
        {
            if (Blocked) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(Map(k));
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static Key Map(K k)
        {
            switch (k)
            {
                case K.W: return Key.W;
                case K.A: return Key.A;
                case K.S: return Key.S;
                case K.D: return Key.D;
                case K.Space: return Key.Space;
                case K.J: return Key.J;
                case K.Kk: return Key.K;
                case K.L: return Key.L;
                case K.I: return Key.I;
                case K.LeftShift: return Key.LeftShift;
                case K.Up: return Key.UpArrow;
                case K.Down: return Key.DownArrow;
                case K.Left: return Key.LeftArrow;
                case K.Right: return Key.RightArrow;
                case K.Np0: return Key.Numpad0;
                case K.Np1: return Key.Numpad1;
                case K.Np2: return Key.Numpad2;
                case K.Np3: return Key.Numpad3;
                case K.Comma: return Key.Comma;
                case K.Period: return Key.Period;
                case K.Slash: return Key.Slash;
                case K.RightCtrl: return Key.RightCtrl;
                case K.RightShift: return Key.RightShift;
                case K.Enter: return Key.Enter;
                case K.NpEnter: return Key.NumpadEnter;
                case K.Escape: return Key.Escape;
                case K.Minus: return Key.Minus;
                case K.Equals: return Key.Equals;
                case K.NpPlus: return Key.NumpadPlus;
                case K.NpMinus: return Key.NumpadMinus;
                case K.Tab: return Key.Tab;
                default: return Key.F1;
            }
        }
#else
        static KeyCode Map(K k)
        {
            switch (k)
            {
                case K.W: return KeyCode.W;
                case K.A: return KeyCode.A;
                case K.S: return KeyCode.S;
                case K.D: return KeyCode.D;
                case K.Space: return KeyCode.Space;
                case K.J: return KeyCode.J;
                case K.Kk: return KeyCode.K;
                case K.L: return KeyCode.L;
                case K.I: return KeyCode.I;
                case K.LeftShift: return KeyCode.LeftShift;
                case K.Up: return KeyCode.UpArrow;
                case K.Down: return KeyCode.DownArrow;
                case K.Left: return KeyCode.LeftArrow;
                case K.Right: return KeyCode.RightArrow;
                case K.Np0: return KeyCode.Keypad0;
                case K.Np1: return KeyCode.Keypad1;
                case K.Np2: return KeyCode.Keypad2;
                case K.Np3: return KeyCode.Keypad3;
                case K.Comma: return KeyCode.Comma;
                case K.Period: return KeyCode.Period;
                case K.Slash: return KeyCode.Slash;
                case K.RightCtrl: return KeyCode.RightControl;
                case K.RightShift: return KeyCode.RightShift;
                case K.Enter: return KeyCode.Return;
                case K.NpEnter: return KeyCode.KeypadEnter;
                case K.Escape: return KeyCode.Escape;
                case K.Minus: return KeyCode.Minus;
                case K.Equals: return KeyCode.Equals;
                case K.NpPlus: return KeyCode.KeypadPlus;
                case K.NpMinus: return KeyCode.KeypadMinus;
                case K.Tab: return KeyCode.Tab;
                default: return KeyCode.F1;
            }
        }
#endif
    }

    /// <summary>
    /// Esquema de teclado. Esquema 0: WASD + Espacio/J/K/L (+Shift correr).
    /// Esquema 1: flechas + teclado numerico 0/1/2/3 (o CtrlDer , . /).
    /// </summary>
    public class KeyboardInput : IInputProvider
    {
        public readonly int Scheme;
        readonly K up, down, left, right, run;
        readonly K[] jump, punch, bomb, pickup;

        public KeyboardInput(int scheme)
        {
            Scheme = scheme;
            if (scheme == 0)
            {
                up = K.W; down = K.S; left = K.A; right = K.D; run = K.LeftShift;
                jump = new[] { K.Space };
                punch = new K[0];  // golpe: clic derecho
                bomb = new K[0]; // la bomba va en el clic izquierdo
                pickup = new K[0]; // agarrar: boton central del raton
            }
            else
            {
                up = K.Up; down = K.Down; left = K.Left; right = K.Right; run = K.RightShift;
                jump = new[] { K.Np0, K.RightCtrl };
                punch = new[] { K.Np1, K.Comma };
                bomb = new[] { K.Np2, K.Period };
                pickup = new[] { K.Np3, K.Slash };
            }
        }

        static bool AnyHeld(K[] ks)
        {
            foreach (var k in ks) if (Keys.Held(k)) return true;
            return false;
        }

        static bool AnyDown(K[] ks)
        {
            foreach (var k in ks) if (Keys.Down(k)) return true;
            return false;
        }

        public InputState Read()
        {
            var s = new InputState();
            float x = (Keys.Held(right) ? 1f : 0f) - (Keys.Held(left) ? 1f : 0f);
            float y = (Keys.Held(up) ? 1f : 0f) - (Keys.Held(down) ? 1f : 0f);
            s.move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            s.jump = AnyHeld(jump);
            s.punch = AnyHeld(punch);
            s.bomb = AnyHeld(bomb);
            s.pickup = AnyHeld(pickup);
            s.run = Keys.Held(run);
            if (Scheme == 0)
            {
                s.clickBomb = true;
                s.bomb = Keys.MouseHeld(0);   // clic izquierdo: bomba
                s.punch = Keys.MouseHeld(1);  // clic derecho: golpe
                s.pickup = Keys.MouseHeld(2); // boton central: agarrar
                s.hasAim = Keys.MouseAim(out s.aimPoint);
            }
            return s;
        }

        public bool JoinPressed() => AnyDown(jump) || AnyDown(punch) || AnyDown(bomb) || AnyDown(pickup) ||
                                     (Scheme == 0 && (Keys.MouseDown(0) || Keys.MouseDown(1) || Keys.MouseDown(2)));

        public string Describe() => Scheme == 0
            ? "WASD mover | Espacio saltar | Clic izq. bomba (1er clic: sacar | 2o: mantener y soltar hacia el puntero) | Clic der. golpe | Clic central agarrar | Shift correr"
            : "Flechas mover | Num0 saltar | Num1 golpe | Num2 bomba | Num3 agarrar";
    }

    /// <summary>Mando (layout Xbox): A saltar, X golpe, B bomba, Y agarrar, gatillos/hombros correr.</summary>
    public class GamepadInput : IInputProvider
    {
#if ENABLE_INPUT_SYSTEM
        public readonly Gamepad Pad;
        public GamepadInput(Gamepad pad) { Pad = pad; }

        public InputState Read()
        {
            var s = new InputState();
            if (Pad == null || !Pad.added) return s;
            Vector2 m = Pad.leftStick.ReadValue();
            Vector2 d = Pad.dpad.ReadValue();
            if (d.sqrMagnitude > m.sqrMagnitude) m = d;
            if (m.magnitude < 0.18f) m = Vector2.zero;
            s.move = Vector2.ClampMagnitude(m, 1f);
            s.jump = Pad.buttonSouth.isPressed;
            s.punch = Pad.buttonWest.isPressed;
            s.bomb = Pad.buttonEast.isPressed;
            s.pickup = Pad.buttonNorth.isPressed;
            s.run = Pad.leftTrigger.ReadValue() > 0.3f || Pad.rightTrigger.ReadValue() > 0.3f ||
                    Pad.leftShoulder.isPressed || Pad.rightShoulder.isPressed;
            return s;
        }

        public static bool AnyButtonDown(Gamepad p) =>
            p.buttonSouth.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame ||
            p.buttonEast.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame ||
            p.startButton.wasPressedThisFrame;

        public static IEnumerable<Gamepad> All()
        {
            foreach (var g in Gamepad.all) yield return g;
        }
#else
        public readonly object Pad;
        public GamepadInput(object pad) { Pad = pad; }
        public InputState Read() => new InputState();
        public static IEnumerable<object> All() { yield break; }
#endif
    }

    /// <summary>Teclas/botones de menu (empezar, volver, ajustar bots).</summary>
    public static class MenuInput
    {
        public static bool Start()
        {
            if (Keys.Down(K.Enter) || Keys.Down(K.NpEnter)) return true;
#if ENABLE_INPUT_SYSTEM
            foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
#endif
            return false;
        }

        public static bool Back()
        {
            if (Keys.Down(K.Escape)) return true;
#if ENABLE_INPUT_SYSTEM
            foreach (var g in Gamepad.all) if (g.selectButton.wasPressedThisFrame) return true;
#endif
            return false;
        }

        public static int BotDelta()
        {
            int d = 0;
            if (Keys.Down(K.Equals) || Keys.Down(K.NpPlus)) d++;
            if (Keys.Down(K.Minus) || Keys.Down(K.NpMinus)) d--;
#if ENABLE_INPUT_SYSTEM
            foreach (var g in Gamepad.all)
            {
                if (g.rightShoulder.wasPressedThisFrame) d++;
                if (g.leftShoulder.wasPressedThisFrame) d--;
            }
#endif
            return d;
        }

        public static int KillsDelta()
        {
            int d = 0;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[Key.PageUp].wasPressedThisFrame) d++;
                if (kb[Key.PageDown].wasPressedThisFrame) d--;
            }
            foreach (var g in Gamepad.all)
            {
                if (g.dpad.up.wasPressedThisFrame) d++;
                if (g.dpad.down.wasPressedThisFrame) d--;
            }
#else
            if (Input.GetKeyDown(KeyCode.PageUp)) d++;
            if (Input.GetKeyDown(KeyCode.PageDown)) d--;
#endif
            return d;
        }
    }
}
