\ Forth ARM
\ Copyright 2014 Braden Shepherdson
\ Version 1

\ This is a Forth system
\ designed to run on ARMv6
\ systems. It consists of this
\ source code file and a binary
\ executable.

: / /MOD SWAP DROP ;

: MOD /MOD DROP ;

: NL 10 ;
: BL 32 ;
: CR NL EMIT ;
: SPACE BL EMIT ;

: NEGATE 0 SWAP - ;

\ Standard words for booleans
: TRUE -1 ;
: FALSE 0 ;
: NOT 0= ;


\ LITERAL compiles LIT <foo>
: LITERAL IMMEDIATE
    ' LIT , \ compile LIT
    ,       \ compile literal
  ;

\ Idiom: [ ] and LITERAL to
\ compute at compile time.
\ Me: This seems dubious. The
\ dict is getting longer.
: ';' [ CHAR ; ] LITERAL ;
: '(' [ CHAR ( ] LITERAL ;
: ')' [ CHAR ) ] LITERAL ;


\ Compiles IMMEDIATE words.
: [COMPILE] IMMEDIATE
    WORD  \ get the next word - yes, word. FIND expects the counted string.
    FIND  \ find it in the dict -- xt flag
    DROP  \ XXX: Dangerous, we're assuming it's found successfully.
    >BODY \  get its codeword
    ,     \ and compile it.
  ;


: RECURSE IMMEDIATE
    LATEST @  \ This word
    >BODY     \ get codeword
    ,         \ compile it
  ;

\ Control structures - ONLY SAFE IN COMPILED CODE!
\ cond IF t THEN rest
\ -> cond 0BRANCH OFFSET t rest
\ cond IF t ELSE f THEN rest
\ -> cond 0BRANCH OFFSET t
\      BRANCH OFFSET2 f rest
: IF IMMEDIATE
    ' 0BRANCH ,
    HERE @      \ save location
    0 ,         \ dummy offset
  ;


: THEN IMMEDIATE
    DUP
    HERE @ SWAP - \ calc offset
    SWAP !        \ store it
  ;

: ELSE IMMEDIATE
    ' BRANCH , \ branch to end
    HERE @     \ save location
    0 ,        \ dummy offset
    SWAP       \ orig IF offset
    DUP        \ like THEN
    HERE @ SWAP -
    SWAP !
  ;


\ BEGIN loop condition UNTIL ->
\ loop cond 0BRANCH OFFSET
: BEGIN IMMEDIATE
    HERE @
;

: UNTIL IMMEDIATE
    ' 0BRANCH ,
    HERE @ -
    ,
;


\ BEGIN loop AGAIN, infinitely.
: AGAIN IMMEDIATE
    ' BRANCH ,
    HERE @ -
    ,
  ;

\ UNLESS is IF reversed
: UNLESS IMMEDIATE
    ' NOT ,
    [COMPILE] IF
  ;



\ BEGIN cond WHILE loop REPEAT
: WHILE IMMEDIATE
    ' 0BRANCH ,
    HERE @
    0 , \ dummy offset
  ;

: REPEAT IMMEDIATE
    ' BRANCH ,
    SWAP
    HERE @ - ,
    DUP
    HERE @ SWAP -
    SWAP !
  ;


: ( IMMEDIATE
    ')' PARSE 2DROP ;

: NIP ( x y -- y ) SWAP DROP ;
: TUCK ( x y -- y x y )
    SWAP OVER ;
: PICK ( x_u ... x_1 x_0 u -- x_u ... x_1 x_0 x_u )
    1+     \ skip over u
    4 *    \ word size
    DSP@ + \ add to DSP
    @      \ fetch
  ;


\ writes n spaces to stdout
: SPACES ( n -- )
    BEGIN
        DUP 0> \ while n > 0
    WHILE
        SPACE 1-
    REPEAT
    DROP
;

\ Standard base changers.
: DECIMAL ( -- ) 10 BASE ! ;
: HEX ( -- ) 16 BASE ! ;


\ Strings and numbers
: U.  ( u -- )
    BASE @ /MOD \ ( r q )
    ?DUP IF \ if q <> 0 then
        RECURSE \ print quot
    THEN
    \ print the remainder
    DUP 10 < IF
        48 \ dec digits 0..9
    ELSE
        10 -
        65 \ hex and other A..Z
    THEN
    + EMIT
;

\ Debugging utility.
: .S ( -- )
    DSP@ \ get stack pointer
    BEGIN
        DUP S0 @ <
    WHILE
        DUP @ U. \ print
        SPACE
        4+       \ move up
    REPEAT
    DROP
;


: UWIDTH ( u -- width )
    BASE @ / \ rem quot
    ?DUP IF   \ if quot <> 0
        RECURSE 1+
    ELSE
        1 \ return 1
    THEN
;



: U.R ( u width -- )
    SWAP   \ ( width u )
    DUP    \ ( width u u )
    UWIDTH \ ( width u uwidth )
    ROT    \ ( u uwidth width )
    SWAP - \ ( u width-uwdith )
    SPACES \ no-op on negative
    U.
;


\ Print padded, signed number
: .R ( n width -- )
    SWAP DUP 0< IF
        NEGATE \ width u
        1 SWAP \ width 1 u
        ROT 1- \ 1 u width-1
    ELSE
        0 SWAP ROT \ 0 u width
    THEN
    SWAP DUP \ flag width u u
    UWIDTH \ flag width u uw
    ROT SWAP - \ ? u w-uw
    SPACES SWAP \ u ?
    IF 45 EMIT THEN \ print -
    U. ;

: . 0 .R SPACE ;
\ Replace U.
: U. U. SPACE ;
\ ? fetches an addr and prints
: ? ( addr -- ) @ . ;



\ c a b WITHIN ->
\   a <= c & c < b
: WITHIN ( c a b -- ? )
    -ROT    ( b c a )
    OVER    ( b c a c )
    <= IF
        > IF   ( b c -- )
            TRUE
        ELSE
            FALSE
        THEN
    ELSE
        2DROP
        FALSE
    THEN
;

: DEPTH ( -- n )
    S0 @ DSP@ -
    4- \ adjust for S0 on stack
;

: ALIGNED ( addr -- addr )
    3 + 3 INVERT AND
;

: ALIGN HERE @ ALIGNED HERE ! ;

: C,
    HERE @ C!
    1 HERE +! \ Increment HERE
;



\ This is tricky. The LHS before DOES> should have called CREATE. That new definition willl
\ look like: DOCOL, LIT, body pointer, EXIT, EXIT. (Yes, two EXITs.)
\ DOES> replaces the first EXIT with the address of the DOCOL it's about to insert.

\ There are three distinct phases here:
\ 1. A defining word (eg. ARRAY) whose definition contains DOES>
\ 2. That defining word is used to create another word.
\ 3. That new word is executed.
\ DOES> runs during compilation of 1., and it needs to set up the second two phases.
: DOES> IMMEDIATE
    \ First compute the address of where we're going to put the DOCOL for the code after DOES>.
    \ Code needs to be compiled into phase 2 that will overwrite the first EXIT in phase 3.

    HERE @ 44 +

    ' LATEST ,
    ' @ ,
    ' >BODY ,
    ' LIT ,
    12 ,
    ' + ,
    ' LIT ,
    , \ Write the address we computed above.
    ' SWAP ,
    ' ! ,
    ' EXIT ,

    \ Now this should be where the HERE pointer above is aimed. Compile DOCOL now.
    \ We're now one-layer compiling instead of ridiculous double-compiling.
    DOCOL ,
    \ And now we return to compiling mode for the DOES> RHS.
;


\ Allocates n bytes of memory. If n is negative, frees the memory instead.
: ALLOT ( n -- )
    HERE +!     \ add n to HERE
  ;

\ Cell and character conversion functions.
: CELLS ( n -- n ) 4 * ;
: CELL+ ( a-addr1 -- a-addr2 ) 4 + ;
: CHAR+ ( c-addr1 -- c-addr2 ) 1+ ;
: CHARS ( n1 -- n2 ) ;

\ The three core defining words, which make use of DOES>.
: CONSTANT  ( x -- ) ( -- x ) CREATE , DOES> @ ;

: VARIABLE ( -- ) ( -- a-addr )
    CREATE 0 , ; \ No need for a DOES> here, returning the address is already the right thing.

: ARRAY ( n -- ) ( index -- a-addr )
    CREATE CELLS ALLOT DOES> SWAP CELLS + ;

\ Turns an address for a counted string into an address/length pair.
: COUNT ( c-addr1 -- c-addr2 u ) DUP C@ SWAP 1+ SWAP ;

: DUMP ( addr len -- )
    BASE @ -ROT \ save the current BASE at the bottom of the stack
    HEX         \ and switch to hex mode

    BEGIN
        ?DUP \ while len > 0
    WHILE
        OVER 8 U.R  \ print the address
        SPACE

        \ print up to 16 words on this line
        2DUP     \ addr len addr len
        1- 15 AND 1+  \ addr len addr linelen
        BEGIN
            ?DUP  \ while linelen > 0
        WHILE
            SWAP       \ addr len linelen addr
            DUP C@     \ addr len linelen addr byte
            2 .R SPACE \ print the byte
            1+ SWAP 1- \ addr len linelen addr -- addr len addr+1 linelen-1
        REPEAT
        DROP ( addr len )

        \ print the ASCII equivalents
        2DUP 1- 15 AND 1+ \ addr len addr linelen
        BEGIN
            ?DUP   \ while linelen > 0
        WHILE
            SWAP DUP C@   \ addr len linelen addr byte
            DUP 32 123 WITHIN IF    \ 32 <= c < 128
                EMIT
            ELSE
                DROP 46 EMIT \ emit a period
            THEN
            1+ SWAP 1-   \ addr len linelen addr -- addr len addr+1 linelen-1
        REPEAT
        DROP \ addr len
        CR

        DUP 1- 15 AND 1+ \ addr len linelen
        TUCK             \ addr linelen len linelen
        -                \ addr linelen len-linelen
        >R + R>          \ addr+linelen len-linelen
    REPEAT

    DROP   \ restore stack
    BASE ! \ restore saved BASE
;


: CASE IMMEDIATE 0 ;
: OF IMMEDIATE
    ' OVER ,
    ' = ,
    [COMPILE] IF
    ' DROP ,
;
: ENDOF IMMEDIATE
    [COMPILE] ELSE ;
: ENDCASE IMMEDIATE
    ' DROP ,
    BEGIN ?DUP WHILE
    [COMPILE] THEN REPEAT
;

\ Creates a dictionary entry with no name.
: :NONAME ( -- xt)
    0 0 (CREATE) \ nameless entry
    LATEST @     \ LATEST holds the address of the link pointer, which is the xt.
    DUP >R \ Set aside the xt.
    >BODY HERE ! \ And adjust HERE to point at the top of the body again.
    DOCOL ,
    R> \ Restore the xt to TOS.
    ] \ compile the definition.
;

\ compiles in a LIT
: ['] IMMEDIATE ' LIT , ;


: DO IMMEDIATE \ lim start --
  HERE @
  ' 2DUP ,
  ' SWAP , ' >R , ' >R ,
  ' > ,
  ' 0BRANCH ,
  HERE @ \ location of offset
  0 , \ dummy exit offset
;


: +LOOP IMMEDIATE \ inc --
  ' R> , ' R> , \ i s l
  ' SWAP , \ ils
  ' ROT , ' + , \ l s'
  ' BRANCH , \ ( top branch )
  SWAP HERE @ \ ( br top here )
  - , \ top ( br )
  HERE @ OVER -
  SWAP ! \ end
  ' R> , ' R> , ' 2DROP ,
;

: LOOP IMMEDIATE \ --
  ' LIT , 1 , [COMPILE] +LOOP ;

: I \  -- i
  R> R> \ ret i
  DUP -ROT >R >R ;

: J \ -- j
  R> R> R> R> DUP \ ( riljj )
  -ROT \ ( r i j l j )
  >R >R \ ( r i j )
  -ROT >R >R \ ( j )
;

\ Drops the values from RS.
: UNLOOP \ ( -- )
  R> R> R> 2DROP >R ;


: S" IMMEDIATE ( -- addr len )
    STATE @ IF \ compiling?
        ' LITSTRING ,
        34 PARSE \ addr len
        DUP ,    \ addr len (and the length compiled in)
        HERE @ \ src len dst
        SWAP   \ src dst len
        DUP HERE +! \ src dst len - move HERE to make room

        0 DO \ src dst
            OVER I + C@ \ src dst val
            OVER I + C! \ src dst
        LOOP
        2DROP
        ALIGN
    THEN
    \ TODO: Write the interpretation version, that copies to HERE without moving the HERE-pointer.
    \ Or can it get away with using the keyboard input buffer?
;

: ." IMMEDIATE ( -- )
    STATE @ IF \ compiling?
        [COMPILE] S"
        ' TELL ,
    ELSE
        \ Just read and print
        34 PARSE \ addr len
        TELL
    THEN
;

\ Empties both stacks and returns to a pristine state.
: ABORT ( ... -- ) S0 @ DSP!   QUIT ;

\ Parses a string at compile-time. At run-time prints it and aborts if the test value is nonzero.
\ This is one of the most meta things I've ever written.
: ABORT" IMMEDIATE ( ... "ccc<quote>" -- )
    [COMPILE] IF
        [COMPILE] S"
        ' TELL ,
        ' CR ,
        ' ABORT ,
    [COMPILE] THEN
;


: ABS ( n -- u ) DUP 0< IF NEGATE THEN ;

\ Reads up to n1 characters into c-addr. Echoes them as they come in.
\ Returns the number of characters actually read. Stops on line termination.
\ XXX: This is slightly busted: it won't return until you press Enter, even if you
\ overrun the buffer.
: ACCEPT ( c-addr +n1 - +n2 )
    DUP >R \ Set aside the original length for later.
    BEGIN DUP 0 > WHILE \ addr remaining
        KEY     \ addr rem key
        DUP 10 = IF
            \ Exit early.
            DROP NIP \ rem
            R> SWAP - \ diff
            EXIT
        THEN
        ROT \ rem key addr
        2DUP C! \ rem key addr
        1+ ROT \ key addr' rem
        1- ROT DROP \ addr' rem'
        DUP .
    REPEAT
    \ If we get here, we ran out of space, so return  the original length.
    2DROP R> \ n1
;




: WELCOME
    ." FORTH ARM" CR
    ." by Braden Shepherdson" CR
    ." version " VERSION . CR
;
WELCOME


