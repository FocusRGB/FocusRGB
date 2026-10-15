/*
  LEDStripTester

  Flashes a strip of WS2812B LEDs through several different patterns
  demonstrating several ways to manipulate the LEDs

*/
#include <WS2812BStrip.h>
#include <Arduino.h>
#define LEDSPIN 5
#define LEDSIZE 32
#define REFRESHRATE 20

using namespace WS2812B;

LED lights[LEDSIZE];
LED single;
LEDStrip strip(lights,LEDSIZE);
int stackTop;

void setup() {

  initLEDStrip(lights, LEDSIZE);
  single.next=&single;

}

void loop() {

  // int input = 2;
  // switch(input){
  //   case 1:
  //     TestInitRGB();
  //     break;
  //   case 2:
  //     ColorCycle();
  //     break;
  //   case 3:
  //     Breath(255,124,49);
  //     break;
  //   default:
  //     break;
  // }
  TestInitRGB();
  ColorCycle();
  Breath(255,124,49);
}

// TestInitRGB turns all LEDs in order to Red, Green and blue
void TestInitRGB(){

  for (int i = 0; i<LEDSIZE; i++){ // All LEDs to Red
    lights[i].red = 255;
    lights[i].green = 0;
    lights[i].blue = 0;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 0; i < LEDSIZE; i++){ // All LEDs to Green
    lights[i].red = 0;
    lights[i].green = 255;
    lights[i].blue = 0;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 0; i<LEDSIZE; i++){ // All LEDs to Blue
    lights[i].red = 0;
    lights[i].green = 0;
    lights[i].blue = 255;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }

}

// ColorCycle() apply color cylce pattern on all LEDs
void ColorCycle(){

  strip.start=&single;

  single.red = 255;
  single.green = 0;
  single.blue = 0;
  strip.write(LEDSPIN);

  for (int i=0 ; i<255 ; i+=5){
    single.green = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 255; i > 0; i-=5){
    single.red = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 0; i < 255; i+=5){
    single.blue = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 255; i > 0; i-=5){
    single.green = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 0; i < 255; i+=5){
    single.red = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 255; i > 0; i-=5){
    single.blue = i;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }

}

// Turn On all LEDs based on a Color and turn them off
/* INPUT
 * int R : Number between 0 and 255 based on RED in RGB
 * int G : Number between 0 and 255 based on Green in RGB
 * int B : Number between 0 and 255 based on Blue in RGB
 */
void Breath(int R, int G, int B){

  // Active wire as a single LED
  strip.start=&single;
  // Turn off all LEDs
  single.red = 0;
  single.green = 0;
  single.blue = 0;
  strip.write(LEDSPIN);
  
  for (int i = 0; i <= 100; i++){ // Turn slowly on all LEDs
    single.red = R*i/100;
    single.green = G*i/100;
    single.blue = B*i/100;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  for (int i = 100; i > 0; i--){ // Turn slowly off all LEDs
    single.red = R*i/100;
    single.green = G*i/100;
    single.blue = B*i/100;
    strip.write(LEDSPIN);
    delay(REFRESHRATE);
  }
  
}

void Rainbow(){



}