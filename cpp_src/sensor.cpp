#include<iostream>

#if defined(_WIN32) || defined(_WIN64)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT
#endif

extern "C" {
    EXPORT bool initialize_sensor(unsigned char activation_byte)  {
      return (activation_byte & 0x04) != 0;
    }

    EXPORT float read_weight_kg(int raw_adc_value, float calibration_factor) {
      float weight = static_cast<float>(raw_adc_value) / calibration_factor;
      return weight;
    }

}