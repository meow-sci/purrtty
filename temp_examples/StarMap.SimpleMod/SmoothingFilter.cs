using System;

namespace StarMap.SimpleExampleMod
{
    public class SmoothingFilter
    {
        private double[] _currentValues;
        private bool _initialized = false;

        public SmoothingFilter(int size)
        {
            _currentValues = new double[size];
        }

        public double[] Apply(double[] target, float smoothness)
        {
            if (target.Length != _currentValues.Length)
            {
                // Resize if needed, though ideally shouldn't happen
                Array.Resize(ref _currentValues, target.Length);
                _initialized = false;
            }

            if (!_initialized)
            {
                Array.Copy(target, _currentValues, target.Length);
                _initialized = true;
                return _currentValues;
            }

            // Smoothness should be between 0 (no smoothing) and close to 1 (high smoothing)
            // We clamp it to ensure we don't stop updating completely
            float s = Math.Clamp(smoothness, 0.0f, 0.99f);
            float factor = 1.0f - s;

            for (int i = 0; i < _currentValues.Length; i++)
            {
                _currentValues[i] = _currentValues[i] + (target[i] - _currentValues[i]) * factor;
            }

            return _currentValues;
        }

        public void Reset()
        {
            _initialized = false;
            Array.Clear(_currentValues, 0, _currentValues.Length);
        }
    }
}








