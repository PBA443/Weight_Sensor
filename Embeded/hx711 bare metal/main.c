#define F_CPU 16000000UL
#include <avr/io.h>
#include <util/delay.h>
#include <stdio.h>
#include <stdint.h>

void uart_init(uint32_t baud) {
    uint16_t ubrr = F_CPU / 16 / baud - 1;
    UBRR0H = (uint8_t)(ubrr >> 8);
    UBRR0L = (uint8_t)ubrr;
    
    UCSR0B = (1 << TXEN0);                  
    UCSR0C = (1 << UCSZ01) | (1 << UCSZ00); 
}

void uart_putchar(char c) {
    while (!(UCSR0A & (1 << UDRE0))); 
    UDR0 = c;
}

void uart_print(const char* str) {
    while (*str) {
        uart_putchar(*str++);
    }
}

int32_t read_HX711_Polling(void) {
    int32_t count = 0;

    // 1. Wait until the data pin (PD2) goes LOW (Signals data is ready)
    // If it stays HIGH, we wait here safely.
    while (PIND & (1 << PIND2)); 

    // Microscopic pause to let the hardware stabilize
    _delay_us(2); 

    // 2. Read the 24 bits
    for (uint8_t i = 0; i < 24; i++) {
        PORTD |= (1 << PORTD3);   // SCK HIGH
        _delay_us(2);             
        
        count = count << 1;       
        
        PORTD &= ~(1 << PORTD3);  // SCK LOW
        _delay_us(2);
        
        if (PIND & (1 << PIND2)) { 
            count++;               
        }
    }
    
    // 3. 25th pulse to lock in Channel A with 128 gain
    PORTD |= (1 << PORTD3);  _delay_us(1);
    PORTD &= ~(1 << PORTD3); _delay_us(1);

    // Sign extension for 24-bit to 32-bit signed conversion
    if (count & 0x800000) {
        count |= 0xFF000000;
    }
    return count;
}

int main(void) {
    // Hardware Setup
    DDRD &= ~(1 << DDD2);    // PD2 (DT) as INPUT
    DDRD |= (1 << DDD3);     // PD3 (SCK) as OUTPUT
    PORTD &= ~(1 << PORTD3); // Ensure SCK starts LOW

    uart_init(9600);         
    
    char buffer[32];
    int32_t raw_weight = 0;

    while (1) {
        // Explicit polling read
        raw_weight = read_HX711_Polling();
        
        // Format and print across serial
        sprintf(buffer, "%ld\r\n", raw_weight);
        uart_print(buffer);
        
        // Give the serial buffer a moment to breathe and the chip time to convert next sample
        _delay_ms(80); 
    }
    return 0;
}