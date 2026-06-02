#define F_CPU 16000000UL
#include <util/delay.h>
#include <stdint.h>

// --- UART Register Mapping ---
#define MY_UBRR0H  (*(volatile uint8_t *)(0xC5))
#define MY_UBRR0L  (*(volatile uint8_t *)(0xC4))
#define MY_UCSR0A  (*(volatile uint8_t *)(0xC0))
#define MY_UCSR0B  (*(volatile uint8_t *)(0xC1))
#define MY_UCSR0C  (*(volatile uint8_t *)(0xC2))
#define MY_UDR0    (*(volatile uint8_t *)(0xC6))

#define PORTD      (*(volatile uint8_t *)(0x2B))
#define DDRD       (*(volatile uint8_t *)(0x2A))
#define PIND       (*(volatile uint8_t *)(0x29))

#define UDRE0  5
#define TXEN0  3
#define RXEN0  4
#define UCSZ01 2
#define UCSZ00 1

#define PORTD3 3 // HX711 SCK
#define PIND2  2 // HX711 DT

void uart_init(uint32_t baud) {
    // Hardcoded for 9600 baud at 16MHz for simplicity
    MY_UBRR0H = 0;
    MY_UBRR0L = 104;
    MY_UCSR0B = (1 << TXEN0);
    MY_UCSR0C = (1 << UCSZ01) | (1 << UCSZ00);
}

void uart_send_byte(uint8_t b) {
    while (!(MY_UCSR0A & (1 << UDRE0)));
    MY_UDR0 = b;
}

// Your existing HX711 polling function remains exactly the same
int32_t read_HX711_Polling(void) {
    int32_t count = 0;
    while (PIND & (1 << PIND2)); 
    _delay_us(2); 

    for (uint8_t i = 0; i < 24; i++) {
        PORTD |= (1 << PORTD3);   _delay_us(2);
        count = count << 1;
        PORTD &= ~(1 << PORTD3);  _delay_us(2);
        
        if (PIND & (1 << PIND2)) { 
            count++;
        }
    }
    PORTD |= (1 << PORTD3);  _delay_us(1);
    PORTD &= ~(1 << PORTD3); _delay_us(1);

    if (count & 0x800000) {
        count |= 0xFF000000;
    }
    return count;
}

int main(void) {
    DDRD &= ~(1 << PIND2);   // Input
    DDRD |= (1 << PORTD3);   // Output
    PORTD &= ~(1 << PORTD3); 

    uart_init(9600);
    int32_t raw_weight = 0;

    while (1) {
        raw_weight = read_HX711_Polling();
        
        // 1. Send the Sync Header Byte
        uart_send_byte(0xAA);
        
        // 2. Slice and send the 32-bit integer (Highest byte to Lowest byte)
        uart_send_byte((raw_weight >> 24) & 0xFF);
        uart_send_byte((raw_weight >> 16) & 0xFF);
        uart_send_byte((raw_weight >> 8)  & 0xFF);
        uart_send_byte(raw_weight & 0xFF);
        
        _delay_ms(80); 
    }
    return 0;
}