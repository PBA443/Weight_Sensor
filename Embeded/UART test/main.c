#define F_CPU 16000000UL // Your microcontroller clock speed
#define TARGET_BAUD 9600 // The baud rate you want to target

// This macro forces the COMPILER to do the math and bit shifts!
#define CALC_UBRR        (((F_CPU / (16UL * TARGET_BAUD))) - 1)
#define UBRR_HIGH_BYTE   (CALC_UBRR >> 8)
#define UBRR_LOW_BYTE    (CALC_UBRR & 0xFF)
#include <util/delay.h>

// Bare-Metal Register Mapping (Using absolute Data Space addresses)
#define MY_UBRR0H  (*(volatile uint8_t *)(0xC5))
#define MY_UBRR0L  (*(volatile uint8_t *)(0xC4))
#define MY_UCSR0A  (*(volatile uint8_t *)(0xC0))
#define MY_UCSR0B  (*(volatile uint8_t *)(0xC1))
#define MY_UCSR0C  (*(volatile uint8_t *)(0xC2))
#define MY_UDR0    (*(volatile uint8_t *)(0xC6))

// Bit Definitions from the Datasheet
#define UDRE0  5
#define RXEN0  4
#define TXEN0  3
#define UCSZ01 2
#define UCSZ00 1

void UART_init(void) {
    
    MY_UBRR0H = UBRR_HIGH_BYTE;
    MY_UBRR0L = UBRR_LOW_BYTE;
    
    MY_UCSR0B = (1 << TXEN0) | (1 << RXEN0);
    
    MY_UCSR0C = (1 << UCSZ01) | (1 << UCSZ00);
}

void UART_send_char(char c) {
  
    while (!(MY_UCSR0A & (1 << UDRE0)));
    
    MY_UDR0 = c;
}

void UART_send_string(char *str) {
    
    while (*str) {
        UART_send_char(*str);
        str++;
    }
}

int main(void) {
  
    UART_init();
    
    while (1) {
        UART_send_string("Hello from Bare-Metal UART!\r\n");
        _delay_ms(5000);
    }
    
    return 0;
}