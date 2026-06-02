#define F_CPU 16000000UL
#include <avr/io.h>
#include <util/delay.h>

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
    while (*str) uart_putchar(*str++);
}

int main(void) {
    uart_init(9600);
    while (1) {
        uart_print("HELLO\r\n");
        _delay_ms(1000);
    }
    return 0;
}