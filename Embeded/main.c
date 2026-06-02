#ifndef F_CPU
#define F_CPU 16000000UL // 16 MHz clock speed
#endif

#include <avr/io.h>
#include <util/delay.h>

int main(void)
{
  // Set PB5 (Digital Pin 13) as an output
  DDRB |= (1 << DDB5);

  while (1)
  {
    PORTB |= (1 << PORTB5);  // Turn LED on
    _delay_ms(200);          // Wait 500ms
    PORTB &= ~(1 << PORTB5); // Turn LED off
    _delay_ms(1000);          // Wait 500ms
  }
}