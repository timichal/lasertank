
              ***************************************************
              ***    Welcome to the LaserTank Update Data     ***
              ***  Utility (LTUDU), Version 6.2.1 (Freeware)  ***
              ***************************************************


  =================
  Distributed Files
  =================

     The LTUpdate.ZIP archive file contains the following files:
        LTUpdate.exe - LTUDU program.
        ReadMeUDU.TXT - Read-only (see Caution 1 below) ReadMe file for the
                          LTUDU (this file).
        VerHistUDU.TXT - LTUDU version history.
        WBDXA44I.DLL - Dynamic Linked Library (DLL), for WinBatch+Compiler
                         2024A, that is required by LTUpdate.exe (from Island
                         Lake Consulting LLC - Ref. http://www.winbatch.com).
                         (This file replaces WBDCC34I.DLL - see the "Install-
                         ation and Removal of this Utility" section below.)
        WWZIP44I.DLL - WIL (Windows Interface Language) Extender DLL that is
                         required, for the Zipping and un-Zipping of files, by
                         LTUpdate.exe (from Island Lake Consulting LLC - Ref.
                         http://www.winbatch.com).  (This file replaces
                         WWZIP34I.DLL - see the "Installation and Removal of
                         this Utility" section below.)
  The LTUpdate.ZIP file was (probably) downloaded from either:
        * The "Download LaserTank Update Levels and High Scores Utility" link
             at the bottom of the "Download New Levels & View High Scores
             Lists" (click on "New Levels & High Scores") page of the
             LaserTank web site (https://laser-tank.com); or,
        * The "More Downloads" page of the LaserTank web site
             (https://laser-tank.com).

     See the "Installation and Removal of this Utility" section below for
  installation instructions.

  ------------------------------------------------------------------------------


  ===========================================
  LaserTank Data Files and Levels Collections
  ===========================================

     In general, LaserTank data files can be located anywhere.  However, to use
  the LTUDU program, it (LTUpdate.exe) and the LaserTank data files (except as
  noted) must be in your LaserTank folder; i.e., the folder that contains the
  LaserTank program (LaserTank.exe).  Your LaserTank folder, including its path,
  will be called "YourLTFolder" (without the quotes) in this discussion; e.g.,
  YourLTFolder = C:\Games\LaserTank\.  All LaserTank related program and data
  file names, including their paths, are case-insensitive.

     LaserTank data is organized and stored in "Levels Collections" that are
  defined by the common root name of their Levels (.LVL) and (local) High
  Scores (.HS) files (and other files); e.g., Ducky-I.lvl and Ducky-I.hs are 
  in the Ducky-I Levels Collection.  These files, and others, are described on
  the "Help Page" of the LaserTank web site (https://laser-tank.com).

     In addition, a Levels Collection may contain Playback (.LPB) files for
  some, or all, of its levels.  These files are optional; i.e., the .LPB file
  for a level will exist only if you choose to record and/or save the level
  that you're working on in the game.  Playback files are described in the:
        * "Recording & Playback" section of the LaserTank Help file (click on
             Help > Index > Recording & Playback on LaserTank's Menu bar); and,
        * "LaserTank Submissions" page on the LaserTank web site
             (https://laser-tank.com).
  The "Standard Name" (SN) for a Playback file (i.e., the one suggested by the 
  LaserTank program) is, CollName_dddd.LPB, where CollName is the name of the
  Levels Collection being played (root name of the .LVL file) and dddd is the
  level's number (4-digits with leading zeros if required); e.g.,
  Ducky-I_0043.lpb is the SN Playback filename for the 43-rd level in the
  Ducky-I Levels Collection (Ducky-I.lvl and Ducky-I.hs files).  These files
  are placed, by default, in YourLTFolder; e.g., YourLTFolder\Ducky-I_0043.lpb
  in the above example.

     "Official" Levels Collections are directly referenced at the top of the
  "Download New Levels & View High Scores Lists" (click on "New Levels &
  High Scores") page of the LaserTank web site (https://laser-tank.com).  Each
  Official Levels Collection has the following additional files: 
        * Global High Scores (.GHS) as described on the "Help Page" of the
                LaserTank web site (https://laser-tank.com).  This file has the
                same root name as the .LVL and .HS files in its collection.
        * Report (.HTML) that specifies the changes made to the collection
                since the last update.  The standard name of this file is
                CollName_Report.HTML where CollName is the collection's name.
  For example: Sokoban-I.lvl, Sokoban-I.hs, Sokoban-I.ghs and
  Sokoban-I_Report.html; plus, any Sokoban-I_dddd.LPB's (dddd = 4-digit
  integer), are in the Sokoban-I (Official) Levels Collection.

     The latest .LVL, .GHS and .HTML files for each Official Levels Collection
  can be individually downloaded from the "New Levels & View High Scores Lists"
  page of the LaserTank web site (https://laser-tank.com).  Or, you can
  download these files (and others in LTUpdates.zip) for all of the Official
  Levels Collections from the "All Levels, GHS & Reports in one zip file" link
  on this web page.

     The .HS files are locally generated or modified, and placed in
  YourLTFolder, by the LaserTank program (LaserTank.exe) when the player
  either: first completes, or beats the old high score of, a level.  As noted
  above, Playback files are optional; i.e., the .LPB file for a level will
  exist only if you record and/or save the level that you are working on or
  complete.

     You can use the 'Editor [F9]' in the LaserTank program. or the programs
  in the LaserTank Utilities Suite (LTUS) or the LaserTank Developer's Utility
  Suite (LTDUS), to create custom (personal) Levels Collections.  These suites
  can be downloaded from the "Applications" sub-page of the "More Downloads"
  page of the LaserTank web site: https://laser-tank.com.

  ------------------------------------------------------------------------------


  ================================
  Description of the LTUDU Program
  ================================

     The LTUDU program, LTUpdate.exe, automates (most of) the work required to
  update your LaserTank data files for the Official Levels Collections defined
  above.

     This program requires a valid (see Note 1 below) Updates.zip archive file
  that was downloaded, to YourLTFolder, from the "All Levels, GHS & Reports in
  one zip file" on the "Download New Levels & View High Scores Lists" page of
  the LaserTank web site (https://laser-tank.com).  To get to this page: go to
  https://laser-tank.com, then click on "English" (or "Français"), and then
  click on "New Levels & High Scores" (or "Tableaux & Records", respectively).

     This archive contains the updated data files (.LVL, .GHS and "Report"
  .HTML files), for each Official Levels Collection.  These files are described
  in the "LaserTank Data Files and Levels Collections" section above.  For
  example, the Sokoban-I.lvl, Sokoban-I.ghs and Sokoban-I_Report.html files in
  Updates.zip are for the Sokoban-I Levels Collection.  These files are used to
  update your LaserTank data files.  Updates.zip also contains a ReadAllUp.txt
  file with information and cautions about the data files in this archive, and
  the date of the last posted update.  See the "How to Use the LTUDU Program"
  section below.

     Updates.zip also contains the following information files that are not
  processed by this program:
        All_Names.txt - List of the Initials currently being used in .GHS and
                        .LPB files.
        GHS_Authors.txt - List of Author's high scores that are better than in
                          the current .GHS files.
        LVL_Name.txt - List of all the Names already used for the levels.

     In addition to the SN Playback (.LPB) files described in the "LaserTank
  Data Files and Levels Collections" section above, the LTUDU program will
  process SN Playback files located in a CollName-named subfolder of
  YourLTFolder; i.e., in YourLTFolder\CollName\CollName_dddd.lpb.
  YourLTFolder\Ducky-I\Ducky-I_0043.lpb in the example in that section.  These
  files are not standard; i.e., they will exist only if you manually create
  this subfolder and move your SN Playback files to it.  SN Playback files in
  either location will be called "Standard Name Extended" (SNE)  Playback
  files.  If both of these .LPB files exist for a given level, the LTUDU
  program will only process the one in the CollName-named subfolder.

     This program issues a warning (see "A" in the "Warning Codes" section
  below) if a valid, accessible, ReadAllUp.txt file couldn't be extracted
  from Updates.zip; or, a valid "Last Update" date couldn't be read from the
  ReadAllUp.txt file that was extracted.  A (default) "Last Update" date of
  45 days before this program's execution date will be used.

     This program issues a warning (see "B" in the "Warning Codes" section
  below) if any file that's extracted from the Updates.zip archive is older
  than:
      * the "Last Update" listed in its ReadAllUp.txt file; or,
      * approximately 45 days before this program's execution date.

     IMPORTANT! The changes specified in the  "Report" .HTML files, and
  implemented in the .LVL and .GHS files in the Updates.zip archive are
  incremental; i.e., they are relative to the previously posted "Last Update"
  listed in the ReadAllUp.txt file in this archive (see "A" in the "Warning
  Codes" section below).  Hence, you should manually verify, or correct, the
  results of the first execution of the LTUpdate.exe program on your computer
  (see the "How to Use the LTUDU Program" section below).  Subsequently, the
  LTUpdate.exe program MUST be used every time a new Updates.zip archive is
  posted in order to keep your LaserTank data files (.LVL, .GHS, and SNE
  Playback) for the Official Levels Collections current and correct (see "A"
  and "B" in the "Warning Codes" section below).  In other words, running this
  program with the current Updates.zip archive, after skipping one, or more,
  Updates.zip archives, could result in erroneous or deleted .HS and/or SNE
  Playback (.LPB) files for one, or more, of the Official Levels Collections.
  Hence, you should manually verify or correct these results.  You may also have
  to manually verify or correct the results of this program if you "manually
  modified" any .LVL file for an Official Levels Collection.  "Manually
  modified" means that the level's playfield was changed by the user after the
  last updates were done; e.g., using the editor that's build into the LaserTank
  program (see Note 2 below).

     At a minimum, the Updates.zip archive that is used must be new (e.g., not
  previously processed by this program).  Hence, none of its .LVL, .GHS or
  "Report" .HTML files can be older than their matching files in YourLTFolder,
  and at least one of its files must be newer than its matching file in
  YourLTFolder.

     As described in the next section, the outputs of this program can be
  copied to the Windows Clipboard and/or a text file (LTUpLog.TXT - in 
  YourLTFolder).  The displayed outputs are wrapped or truncated to 80 
  character lines.  Expand a displayed dialog box to full screen (e.g., by 
  clicking on the 'box' symbol in its upper right-hand corner) to possibly
  display more of a long path and file name.  The full lengths of path and file 
  names will be copied to the output (Updates Log) file and the Clipboard if
  required.

     Keyboard Operations for Displayed Dialog Boxes: When a dialog box opens,
  one of its items (a PushButton, ListBox, CheckBox or RadioButton) is
  "selected".  Press the "Tab" key to select the next item, or press
  (simultaneously) the "Shift"+"Tab" keys to select the previous item.  Use the
  "SpaceBar" to Check/Un-Check CheckBoxes.  Use the "Arrow" keys to navigate
  (select/deselect sub-items) within ListBox's and sets of RadioButton's.
  Press the "SpaceBar" to choose (highlight) the selected item in a ListBox, 
  and press it again to deselect the item.  Press the "Enter" key or (in some 
  cases) the "SpaceBar" to exit the dialog box with its current setting(s).

  ------------------------------------------------------------------------------


  ===========================
  What the LTUDU Program Does
  ===========================

     Assuming normal operation (i.e., no problems or errors are encountered)
  this program performs the following operations:

        Op 1: Extracts the relevant files in Updates.zip (see Note 3 below) to a
          ~TempLTUDU subfolder of YourLTFolder (see Note 4 below); and defines,
          compiles and displays an 'Updated or New Files List' (UNFL).  A UNFL
          contains the names of the .LVL, .GHS and .HTML files in
          YourLTFolder\Updates.zip that are either not in, or newer than those
          in, YourLTFolder.  In this display you can select either, neither or
          both of the following backup methods for the files on the Backup File
          List (BFL) defined in Op(eration) 2 below (see Note 5 below also):
              a) (Default) Copy them to a LaserTank Data Backup Archive file,
                LTDataBackup.ZIP, in YourLTFolder.  This file will be created
                if it doesn't exist.  If it already exists (in YourLTFolder),
                the previous version of this archive will be replaced without
                an option to back it up.  You will be given the opportunity
                to move or rename it.  Otherwise, it will probably become 
                obsolete.  If you don't select this option, any existing
                YourLTFolder\LTDataBackup.ZIP file will probably become
                obsolete.
              b) Append "_Old" (without the quotes) to the right side of the
                root name of each file.  For example, LaserTank.lvl is backed up
                as LaserTank_Old.lvl.  Any existing "_Old" backup files in
                YourLTFolder will be replaced without an option to save them.
                Other "_Old" backup files in YourLTFolder will not be changed or
                deleted; hence, they will probably become obsolete.  If you
                don't select this option, any "_Old" backup file in YourLTFolder
                will probably become obsolete.
          This dialog box also allows you to open ReadMeUDU.TXT (to view only)
          and/or LTUpLog.TXT (to view or edit) if they exist and are accessible
          (see Note 6 below).

        Op 2: Generates a Backup File List (BFL) containing the names of all of
          the:
              * Files in YourLTFolder that are on the UNFL.
              * .HS files in YourLTFolder that could be changed; i.e., that
               correspond to the .LVL files on the UNFL.
              * "Standard Name Extended" (SNE) Playback (.LPB) files that will
               be deleted, replaced, or renamed and (possibly) moved.  Usually
               these files are for the Modified, Deleted, "Moved from" and
               "Moved to" levels in the .LVL files on the UNFL.
          First this list, without its SNE Playback (.LPB) files (if any), is
          displayed in a "... Backup Files List and Confirm Backups" dialog box.
          These files will be backed up (or not) as specified in Op(eration) 1
          above.  Changed SNE Playback (.LPB) files (if any) will be added to
          these backups when appropriate.  This dialog box also allows you to
          open (to view only) the ReadMeUDU.TXT file (see Note 6 below).

        Op 3: Copies the files in the YourLTFolder\~TempLTUDU subfolder that are
          on the UNFL to YourLTFolder.  The existing files in YourLTFolder will
          be replaced without warning.  Other files in this archive (e.g.,
          All_Names.txt, GHS_Authors.txt, Levels_Name.txt) are not extracted.

        Op 4: Deletes the (local) high scores (.HS file entries) of the Modified
          and Deleted levels in the .LVL files on the BFL (as specified in their
          corresponding "Report" .HTML files).  See Notes 7, 8 and 9 below.

        Op 5: Moves the (local) high scores (.HS file entries) of the Moved
          levels in the .LVL files on the BFL (as specified in their
          corresponding "Report" .HTML files - see Note 7 below).  The high
          scores of these levels are deleted from the "Moved from" files (see
          Note 8 below).  The required "Moved to" .HS files will be created (in
          YourLTFolder) if they don't already exist

        Op 6: Optionally, and if it exists, deletes the SNE Playback (.LPB) file
          for each Modified level in the .LVL files on the BFL.  This deletion
          is optional because the existing SNE Playback (.LPB) file (or a
          slightly modified version of it) may still work with the modified
          level.

        Op 7: Deletes any existing SNE Playback (.LPB) files for the Deleted
          levels (see Note 9 below) in the .LVL files on the BFL.

        Op 8: Renames, changes the level numbers in the headers of, and
          (possibly) moves, the SNE Playback (.LPB) files for the Moved levels
          in the .LVL files on the BFL - see Note 10 below.

        Op 9: Sets (or resets) the size of every updated or created .HS file to
          either (user selected):
              c) (Default) Ten (bytes/level) times the highest completed level
                number in it (i.e., to 10*Nhc bytes where Nhc is its highest
                level number with Moves greater than zero).  If no levels are
                completed in a .HS file (i.e., Nhc = 0), it's deleted (or not
                created).  Or,
              d) Ten (bytes/level) times the number of levels in its
                corresponding .LVL file.  This will make it easier for you to
                identify and access the levels that you have not completed (see
                Note 11 below).
          This choice is reversible.

        Op 10: Optionally documents all of the actions taken, and the changes
          made (or considered) by this program.  This "Updates Log" information
          can be viewed and/or copied to a text file, LTUpLog.TXT, in
          YourLTFolder (see Note 6 below).  If YourLTFolder\LTUpLog.TXT doesn't
          exist, it will be created.  If YourLTFolder\LTUpLog.TXT exists, you
          can open it in the associated (registered) application for .TXT files
          (see Note 6 below) before writing the new Updates Log information to
          it.  Then/Else, you can append the new Updates Log information to the
          end (after the old information), or insert it at the beginning
          (before the old information), of an existing YourLTFolder\LTUpLog.TXT
          file.  Or, you can replace the contents of an existing
          YourLTFolder\LTUpLog.TXT file with the new Updates Log information
          from the current execution of this program.

        Op 11: Optionally copies the new Updates Log information, from
          Op(eration) 10 above, to the Windows Clipboard (after a warning, with
          opt-out, if it's not empty).

        Op 12: Optionally opens the:
              e) Updates Log file (if it exists) - YourLTFolder\LTUpLog.TXT
                (for viewing or editing), that was created or modified in
                Op(eration) 10 above (see Note 6 below).
              f) ReadMe file for this program (if it exists) -
                YourLTFolder\ReadMeUDU.TXT (this file) (see Note 6 below).
              g) Descriptions of any Warning codes that may be generated.
 
    This program is spyware and adware free, and it does not connect to any
  internet servers.

  ------------------------------------------------------------------------------


  ============================
  How to Use the LTUDU Program
  ============================

     To use the LTUDU program (see Note 1, and Cautions 2 and 3 below):

        Step 1: Download the "All Levels, GHS & Reports in one zip file" Updates
          archive (Updates.zip) to YourLTFolder from the "Download New Levels &
          View High Scores Lists" page of the LaserTank web site
          (https://laser-tank.com).  See the "Description of the LTUDU Program"
          section above).

        Step 2: Make sure that the Official Levels Collection's files (see the
          "LaserTank Data Files and Levels Collections" section above) are not
          Read-only, or open by other applications in Read-deny mode.  Also,
          make sure that your existing Standard Name Extended (SNE) Playback
          (.LPB) files for the levels in the Official Levels Collections are not
          Read-only, or open by other applications in Read-deny mode.

        Step 3: If backup method "a" is to be specified in Op(eration) 1 in the
          "What the LTUDU Program Does" section above, and the LTDataBackup.ZIP
          file exists in YourLTFolder, make sure that it's not: Read-only, or
          open by another application in Read-deny mode.  The existing
          YourLTFolder\LTDataBackup.ZIP file will be replaced after you are
          given an opportunity to move or rename it.  Also, you can move or
          rename it now (before running this program) if you want to preserve
          its contents.

        Step 4: If backup method "b" is to be specified in Op(eration) 1 in the
          "What the LTUDU Program Does" section above, make sure that all of the
          existing append "_Old" backup files in YourLTFolder (e.g., the files
          with root names ending "_Old") are not: Read-only, or open by other
          applications in Read-deny mode.  If required, the existing "_Old"
          backup files in YourLTFolder will be replaced (overwritten) without an
          option to back them up.  So, move or rename them now (before running
          this program) if you want to save them.

        Step 5: If the updating of an existing Updates Log file
          (YourLTFolder\LTUpLog.TXT) is to be specified in Op(eration) 10 in the
          "What the LTUDU Program Does" section above, make sure that it's not:
          Read-only, or open by another application in Read-deny mode.  You will
          be given the option to backup an existing YourLTFolder\LTUpLog.TXT
          file before it's modified or replaced.

        Step 6: Run the LTUDU program, LTUpdate.exe, and follow its prompts.  If
          this is the FIRST EXECUTION OF THIS PROGRAM on your computer (since
          LaserTank was installed), click on the "No" button if the Warning "B"
          prompt appears (or click on its "Yes" button each time it appears),
          and see Step 7 below.  Whenever you run this program, it's HIGHLY
          RECOMMENDED that you select at least one backup method in Op(eration)
          1 in the "What the LTUDU Program Does" section above, and at least one
          "create/update" the Updates Log file (and/or its information) option
          is specified in Op(eration)s 10 and 11 in that section.

        Step 7: If this is not an "incremental" update (e.g., this is the first
          time that this program is being run on your computer - see Warning
          Code B-2 below), you should manually verify the updates for all of the
          (deleted and) replaced, and moved, levels that were specified in the
          "Report" .HTML files (in YourLTFolder).  Specifically, you should
          check the high scores of, and the SNE Playback (.LPB) files for, these
          levels.  You will have to manually correct any errors.

     If you start a second instance of the LTUDU program (e.g., by
  double-clicking on its desktop icon when it's already running), you will be
  prompted to (optionally) open either: the ReadMe file for the LTUDU (this
  file), or the current Updates Log file (YourLTFolder\LTUpLog.TXT - see
  Op(eration) 10 in the "What the LTUDU Program Does" section above), if they
  exist.  The selected file is opened in the associated (registered) application
  for .TXT files (see Note 6 below).

     The execution of this program is paused several times to display
  information, and allow user input.  Also, it may be paused to allow the user
  to correct an error condition.  Unless specifically authorized in the
  displayed dialog box, you must not: delete, modify, move, rename, change the
  accessibility of (i.e., open in another program in Read-deny mode), or change
  the attributes of (e.g., make Read-only):
        * Any file that has been or could be referenced, replaced, deleted or
            modified by this program; or,
        * The ~TempLTUDU subfolder of YourLTFolder (see Note 3 below) or any of
            its files.
  Also, you must not add any sub-subfolders to this ~TempLTUDU subfolder.  Any
  of these actions could produce FATAL PROGRAM ERRORS, ERRONEOUS RESULTS, and/or
  IRREVERSIBLE LOSS of information (unless this program, or you, have backed up
  your LaserTank data files as described in Op(eration)s 1 and 2 in the "What
  the LTUDU Program Does" section above, and Note 4 below.

     The changes specified and implemented in the files in the Updates.zip
  archive are incremental (i.e., they are relative to the previously posted
  update).  Hence, after the results of its first execution are verified
  (manually), the LTUpdate.exe program MUST be used every time a new Updates.zip
  archive is posted (normally at the beginning of every month) in order to keep
  your LaserTank .LVL, .GHS, .HS and SNE Playback (.LPB) files for the Official
  Levels Collections current and correct.  In other words, running this program
  with the current Updates.zip archive, after skipping one (or more) Updates.zip
  archive(s), could result in ERRONEOUS and/or DELETED .HS and/or SNE Playback
  (.LPB) files.

     See Note 2 (below) if you have manually modified the Playfield of any level
  in any Levels file of any Official Levels Collection in YourLTFolder since
  the last time this program was (successfully) run.

  ------------------------------------------------------------------------------


  ==========================================
  Premature Termination of the LTUDU Program
  ==========================================

     The execution of the LTUDU program (LTUpdate.exe) can be (prematurely)
  terminated by pressing the "Ctrl"+"Pause/Break" keys (simultaneously), and
  then clicking on the "OK" button (or pressing the "Enter" key) to clear the
  WinBatch "Error: 194" dialog box if it pops up.  If the program is minimized
  (e.g., to a button on the Taskbar), you may have to open it (e.g., click on
  its Taskbar button or select it with the "Alt"+"Tab" keys combination) before
  pressing the "Ctrl"+"Pause/Break" keys combination.  WARNING! Pressing the
  "Ctrl"+"Pause/Break" keys combination may terminate any non-minimized program
  that uses the WBDCC44I.DLL file (see the "Installation and Removal of This
  Utility" section below).

     PREMATURE TERMINATION WARNING: The LTUpdate.exe program is "prematurely"
  terminated if it stops (or is stopped by the user, see the above paragraph)
  before it is finished and without the user clicking on the "Exit", "Cancel"
  or "X" button in a dialog box, or pressing a hot-key(s) (e.g., "Esc" or
  "Alt"+"F4") to close a dialog box.  Before rerunning this program (if
  possible), you should (must):

        * Manually delete the ~TempLTUDU subfolder of YourLTFolder (if it still
         exists) and its contents (see Note 3 below).  Since this subfolder may
         contain useful files, you may want to move its contents to another
         temporary location until the problem that caused the premature
         termination of the program is resolved.

        * Assuming that you specified or otherwise generated backup files (see
         Op(eration)s 1 and 2 in the "What the LTUDU Program Does" section
         above, and Note 4 below), use them to restore all of the original,
         Official Levels Collections files (see the "LaserTank Data Files and
         Levels Collections" section above) in YourLTFolder; and the original
         SNE Playback (.LPB) files.  If you didn't specify any backup files,
         you're on your own.  Rerunning this program with the same Updates.zip
         archive (if possible), without restoring the original LaserTank .LVL,
         .GHS, .HS and SNE Playback (.LPB) files, could produce ERRONEOUS
         RESULTS in, and/or IRREVERSIBLE LOSS of, .HS and/or SNE Playback (.LPB)
         files.

  ------------------------------------------------------------------------------


  =====
  Notes
  =====

     Note 1: This program assumes that the Updates.zip file in YourLTFolder is a
  valid (e.g., unmodified) "All Levels, GHS & Reports in one zip file" updates
  archive that was downloaded from the "New Levels & View High Scores Lists"
  page of the LaserTank web site (https://laser-tank.com).  In other words all
  of the contents of this archive are valid files for their types (i.e., their
  contents are consistent with their extensions), and files that have (or
  contain) the same root name are "corresponding" files (i.e., they contain data
  for the same collection of levels); e.g., Sokoban-I.lvl, Sokoban-I.hs,
  Sokoban-I.ghs and Sokoban-I_Report.html are for the (Official) Sokoban-I
  Levels Collection.  Each file in the Updates.zip archive is assumed to contain
  data for no more than 4,096 levels.  This limit is currently 2,030 in the
  LaserTank Levels Collection and 2,000 in the others.

     Note 2: This program will delete the high scores and (optionally) the
  Standard Name Extended (SNE) Playback (.LPB) files (if any) for levels in the
  .LVL files in the Updates.zip file that were manually modified since the last
  time it was successfully run.  For this process, "manually modified" means
  that the level's playfield was changed by the user (e.g., using the editor
  that is build into the LaserTank program).  To avoid losing the high scores
  for these modified levels, you should make SNE Playback (.LPB) files when you
  solve them, and backup these files when asked by the program.  You can then
  restore, and use, these backup Playback (.LPB) files to restore the high
  scores for these modified levels.

     Note 3: Nominally Updates.zip contains the .lvl, .ghs and .html files for
  all of the current Official Levels Collections, and ReadAllUp.txt.  Other
  files in this archive (e.g., All_Names.txt, GHS_Authors.txt, Levels_Name.txt)
  are not extracted.

     Note 4: This program creates a ~TempLTUDU subfolder in YourLTFolder to
  temporarily hold the files extracted from the Updates.zip archive and the
  older .HS and Standard Name Extended (SNE) Playback (.LPB) files that could
  be changed or deleted.  This subfolder, and its contents, are deleted just
  before the execution of this program is completed.  If this program is
  prematurely terminated (e.g., by a fatal error condition), you may have to
  manually delete (or rename or move) this subfolder and its contents.  See
  the "Premature Termination Warning" section above.

     Note 5: In addition to the optional backups described in Op(eration) 1 in
  the "What the LTUDU Program Does" section above, you should manually backup
  all of the .LVL, .GHS and .HS files for Official Levels Collections in
  YourLTFolder (using alternate methods) before executing this program.  Also,
  to be completely safe, you should manually backup all of your Standard Name
  Extended (SNE) Playback (.LPB) files for the levels in the Official Levels
  Collections (using alternate methods) before executing this program.  You may
  need these backup files if this program is prematurely terminated (e.g., by a
  fatal error condition), or if an unforeseen processing error occurs.  See the
  "Premature Termination Warning" section above.

     Note 6: If they exist, this program assumes that ReadMeUDU.TXT and
  LTUpLog.TXT in YourLTFolder are standard text files.  Also, this program
  assumes that .TXT files are associated with (registered to) an existing
  application on your computer.  The default is the Windows Notepad
  (NOTEPAD.EXE) that is installed (by default) in all versions of Windows.

     Note 7: This program reads the "Report" .HTML files (that were extracted
  from the Updates.zip archive) as text files.  It assumes that the text you see
  when any of these files are opened in an HTML viewer/editor is an accurate
  representation of the text in the file; e.g., that a space is a space (" ")
  and not the HTML character entity "&nbsp;" (without the quotes).

     Note 8: Deleted high score in Op(eration)s 4 or 5 in the "What the LTUDU
  Program Does" section above may be replaced if the level is replaced.

     Note 9: This program considers that a level is "Deleted" (and hence not
  replaced) if, and only if: it is or becomes Not Rated, and the first 13
  characters of its name is or becomes "Deleted Level" (case-sensitive).

     Note 10: If the Standard Name Extended (SNE) Playback (.LPB) file for a
  Moved level exists, it will be (renamed, changed and) placed (or left) in
  the relevant CollectionName-named subfolder of YourLTFolder (see Note 1
  above) if this subfolder exists.  Otherwise, it will be (renamed, changed
  and) placed (or left) in YourLTFolder.

     Note 11: Assume that you are running the LaserTank program with the
  Ducky-I Levels Collection (Ducky-I.lvl) that has Nlvl levels, and Nhc (<=
  Nlvl) is the highest completed level number in Ducky-I.hs.  By default, the
  size of Ducky-I.hs is 10 (bytes/level) times Nhc (10*Nhc bytes).  When you
  select "View High Scores" on LaserTank's "Game" menu (or press the "v" key),
  only levels 1 to Nhc are listed.  Hence, only these levels can be accessed.
  If Nhc < Nlvl, the uncompleted levels between (and including) the Nhc-th and
  the Nlvl-th can't be accessed.  However, if you selected option "d" in
  Op(eration) 9 in the "What the LTUDU Program Does" section above and the size
  of Ducky-I.hs was set to 10*Nlvl (> 10*Nhc) bytes, all Nlvl of Ducky-I.lvl's
  levels will be listed; and, hence, the uncompleted levels between (and
  including) the Nhc-th and Nlvl-h can be easily identified and accessed.

  ------------------------------------------------------------------------------


  ========
  Cautions
  ========

     Caution 1: The ReadMeUDU.TXT file may be used by the LTUpdate.exe program
  to display descriptions of it's operations.  Deleting or modifying this file
  may produce erroneous descriptions or error messages.

     Caution 2: Editing or changing the date/time of any file that could be
  referenced, modified or replaced by this program could produce ERRONEOUS
  RESULTS and/or IRREVERSIBLE LOSS of information (unless this program, or you,
  have backed up your LaserTank data files as described in Op(eration)s 1 and 2
  in the "What the LTUDU Program Does" section, and Note 6, above).

     Caution 3: The execution of this program is paused several times to display
  information, and allow user input.  Also, it may be paused to allow the user
  to correct an error condition.  Unless specifically authorized in the
  displayed dialog box, you must not: delete, modify, move, rename, change the
  accessibility of (i.e., open in another program in Read-deny mode), or change
  the attributes of (e.g., make Read-only):
        * Any file that has been or could be referenced, replaced, deleted or
            modified by this program; or,
        * The ~TempLTUDU subfolder of YourLTFolder (see Note 5 above) or any of
            its files.
  Also, you must not add any sub-subfolders to this ~TempLTUDU subfolder.  Any
  of these actions could produce FATAL PROGRAM ERRORS, ERRONEOUS RESULTS, and/or
  IRREVERSIBLE LOSS of information (unless this program, or you, have backed up
  your LaserTank data files as described in Op(eration)s 1 and 2 in the "What
  the LTUDU Program Does" section, and Note 6, above).

  ------------------------------------------------------------------------------


  =============
  Warning Codes
  =============

     The following Warning codes may be generated by the LTUDU program
  (YourLTFolder is defined in the "Description of the LTUDU Program" section
  above):

        A) For unknown reasons, either:
             1) A valid, accessible, ReadAllUp.txt file could not be extracted
               from the "All Levels, GHS & Reports in one zip file" updates
               archive that was used; or,
             2) A valid "Last Update" date couldn't be read from a valid
               ReadAllUp.txt file that was extracted to the ~TempLTUDU subfolder
               of YourLTFolder.
          A default value of approximately 45 days (between one and two months)
          before this program was executed was used as the "Last Update" date.

        B) One, or more, of the original files in YourLTFolder were older than
          the "Last Update" date listed in the ReadAllUp.txt file in the "All
          Levels, GHS & Reports in one zip file" updates archive (Updates.zip)
          that was used with this program; and/or, see Warning "A" above.
          Hence, either:
             1) This was the first time that this program was executed on your
               computer (since LaserTank was installed).  Proceeding after
               receiving this warning was probably harmless; or,
             2) This update was NOT INCREMENTAL; i.e., one, or more, posted
               Updates.zip files were skipped between the one that was used for
               this execution and the one that was used for the previous
               execution of this program.  Proceeding under this condition
               could have produced ERRONEOUS RESULTS in, and/or IRREVERSIBLE
               LOSS of, High Scores (.HS) and/or Standard Name Extended (SNE)
               Playback (.LPB) files, or information (see the Warning Note
               below).  You should manually verify the updates as described in
               Step 7 in the "How to Use the LTUDU Program" section above.

        C) One, or more, of the relevant .LVL, .GHS or "Report" .HTML files that
          were originally in YourLTFolder were Read-only and/or they were open
          by other applications in Read-deny mode.  For some of these files the
          Read-only attribute and/or Read-deny condition may have been removed
          by the user, and they were updated, normally, by the program.  Also,
          some of these files may have been deleted, moved or renamed by the
          user.  However, the Read-only and/or Read-deny conditions of one or
          more, but not all, of these files were not removed, or they were not
          deleted.  Hence, these files remained unchanged, and they probably
          became invalid and/or produced a "hard to reverse" LOSS of
          information (see the Warning Note below).

        D) One, or more, of the relevant High Scores (.HS) files that were
          originally in YourLTFolder did not have a corresponding Levels (.LVL)
          file.  For some, but not all, of these .HS files a corresponding,
          valid and accessible .LVL file was restored by the user, and these
          files were processed normally.  However, one or more of these .HS
          files was unchanged, and they probably became invalid and/or produced
          a "hard to reverse" LOSS of information (see the Warning Note below).

        E) One, or more, of the relevant High Scores (.HS) files that were
          originally in YourLTFolder were Read-only and/or they were open by
          other applications in Read-deny mode.  For some of these files the
          Read-only attribute and/or Read-deny condition may have been removed
          by the user, and they were processed, normally, by the program.  Also,
          some of these files may have been deleted, moved or renamed by the
          user.  However, the Read-only and/or Read-deny conditions of one or
          more, but not all, of these files were not removed, or they were not
          deleted.  Hence, these files remained unchanged, and they probably
          became invalid and/or produced a "hard to reverse" LOSS of
          information (see the Warning Note below).

        F) One, or more, of the relevant .LVL, .GHS or "Report" .HTML backup
          files that were originally in YourLTFolder (e.g., the ones with "Old"
          (without the quotes) appended to the right side of their root names)
          were Read-only and/or they were open by other applications in
          Read-deny mode.  For some of these files the Read-only attribute
          and/or Read-deny condition may have been removed by the user, and they
          were processed, normally, by the program.  Also, some of these files
          may have been deleted, moved or renamed by the user.  However, the
          Read-only and/or Read-deny conditions of one or more, but not all, of
          these files were not removed, or they were not deleted.  Hence, these
          files remained unchanged, and they probably became invalid and/or
          produced an IRREVERSIBLE or "hard to reverse" LOSS of information (see
          the Warning Note below).

        G) One, or more, of the relevant, High Scores (.HS) backup files that
          were originally in YourLTFolder; i.e., the ones with "_Old" without
          the quotes) appended to the right side of their root names, were
          Read-only and/or they were open by other applications in Read-deny
          mode.  At least one of these files was deleted, moved or renamed by
          the user before the updating was started.  This could have produced
          an IRREVERSIBLE LOSS of high scores information (see the Warning Note
          below).

        H) The original LaserTank Data Backup Archive file (LTDataBackup.ZIP) in
          YourLTFolder was Read-only and/or it was open by another application
          in Read-deny mode.  Hence, this program could not update (replace) it.
          The updating of this archive was skipped (deselected by the user).
          This could have produced an IRREVERSIBLE LOSS of information (see the
          Warning Note below).  Also, since it was not changed, the files in
          this backup archive file probably became obsolete or invalid.  This
          (original) archive may have been deleted, moved or renamed by the
          user.

        I) Since updating it was not specified the original LaserTank Data
          Backup Archive file (LTDataBackup.ZIP) in YourLTFolder probably became
          obsolete; i.e., one or more of its files probably became invalid.

        J) No backup method was (or remained) selected for the relevant
          LaserTank .LVL, .GHS, "Report" .HTML, .HS, and Standard Name Extended
          (SNE) Playback (.LPB) files.  Hence, they WERE NOT BACKED UP before
          they were replaced.  This could have produced an IRREVERSIBLE LOSS of
          information (see the Warning Note below).  Also, since they were not
          updated, any existing backup files for these files, and/or the files
          in an existing LaserTank Data Backup Archive file (LTDataBackup.ZIP),
          probably became invalid.

        K) One, or more, of the updated Levels (.LVL) files do not have
          corresponding "Report" files.  Hence, the high scores of Modified or
          Deleted levels in, or levels Moved from, these .LVL files (if any)
          could not be deleted by this program.  However, this program will copy
          the high scores of any levels that are moved to these files.

        L) For unknown reasons, the original LaserTank Data Backup Archive file
          (LTDataBackup.ZIP) in YourLTFolder could not be deleted.  Hence, this
          program could not update it.  The specified updating of this file was
          skipped (deselected by the user).  This could have produced an
          IRREVERSIBLE LOSS of information (see the Warning Note below).  Also,
          since it was not changed, the files in this LTDataBackup.ZIP file
          probably became obsolete or invalid.  This (original) archive may
          have been deleted, moved or renamed by the user.

        M) For unknown reasons, a LaserTank Data Backup Archive file
          (LTDataBackup.ZIP) could not be created, or recreated, in
          YourLTFolder using the relevant LaserTank data files that were
          originally in YourLTFolder.  The specified creation or replacing of
          this file was skipped (deselected by the user).  This could have
          produced an IRREVERSIBLE LOSS of information (see the Warning Note
          below).  Also, since it was not changed, the files in an existing
          Archive file probably became invalid.  If you saved the "Error Code"
          that was returned by WinBatch's "Zipper" Extender (WWZIP44I.DLL) - see
          the web site referenced for this file in the "Distributed Files"
          section above.

        N) For unknown reasons, at least one of the relevant LaserTank .LVL,
          .GHS or .HS files that were originally in YourLTFolder could not be
          backed up by appending "_Old" to the right side of their root names.
          This could have produced an IRREVERSIBLE LOSS of information (see the
          Warning Note below).  Also, since they were not updated, any existing
          backup files for these files probably became invalid.

        O) For unknown reasons, one or more of the relevant High Scores (.HS)
          files that were originally in YourLTFolder could not be copied to its
          ~TempLTUDU subfolder.  This could have produced ERRONEOUS RESULTS for,
          and/or IRREVERSIBLE LOSS of, high scores information for Moved levels
          (if any).  (See the Warning Note below.)

        P) By user request, one or more Standard Name Extended (SNE) Playback
          (.LPB) files for a Modified level were not deleted.  These files may
          not be valid for their Modified levels.

        Q) At least one Standard Name Extended (SNE) Playback (.LPB) file for a
          Modified or Deleted level was Read-only and/or it was open by another
          application in Read-deny mode.  Hence, it could not be deleted by this
          program as requested by the user (for a Modified level) or required
          (for a Deleted level).   For some of these files the Read-only
          attribute and/or Read-deny condition may have been removed by the
          user, and they were deleted, normally, by the program.  Also, some of
          these files may have been deleted, moved or renamed by the user.
          However, the Read-only and/or Read-deny conditions of one or more of
          these files were not changed, or they were not deleted.  Hence, they
          probably became invalid.

        R) At least one Standard Name Extended (SNE) Playback (.LPB) file for
          a "Moved to" level was Read-only and/or it was open by other
          applications in Read-deny mode.  Hence, this program could not delete
          it (as required because its corresponding "Moved from" level SNE
          Playback (.LPB) file doesn't exist).  For some of these files the
          Read-only attribute and/or Read-deny condition may have been removed
          by the user, and they were deleted, normally, by the program.  Also,
          some of these files may have been deleted, moved or renamed by the
          user.  However, the Read-only and/or Read-deny conditions of one or 
          more of these files were not changed, or they were not deleted.
          Hence, they probably became invalid.

        S) At least one Standard Name Extended (SNE) Playback (.LPB) file for
          a "Moved from" level was Read-only and/or it was open by another
          application in Read-deny mode.  Hence, it could not be renamed and
          moved by this program.  For some of these files the Read-only
          attribute and/or Read-deny condition may have been removed by the
          user, and they were renamed and moved, normally, by the program.
          Also, some of these files may have been deleted, moved or renamed by
          the user.  However, the Read-only and/or Read-deny conditions of one
          or more of these files were not changed, or they were not deleted.
          Hence, these files and the SNE Playback (.LPB) files for their
          designated "Moved to" levels (if they exist) probably became invalid.

        T) At least one Standard Name Extended (SNE) Playback (.LPB) file for a
	  "Moved to" level was Read-only and/or it was open by other application
          in Read-deny mode.  Hence, it could not be replaced by its designated
          (renamed and modified) "Moved from" levels SNE Playback (.LPB) file.
          These "Moved from" and "Moved to" SNE Playback (.LPB) files probably
          became invalid.  Some of these files may have been manually deleted,
          moved or renamed by the user.

        U) For unknown reasons, at least one of your relevant Standard Name
          Extended (SNE) Playback (.LPB) files could not be added to the
          LaserTank Data Backup Archive file (LTDataBackup.ZIP) in YourLTFolder.
          This could have produced an IRREVERSIBLE LOSS of information (see the
          Warning Note below).  Older versions of all/any .LPB files were
          deleted along with the original version of this archive file.  If you
          saved the "Error Code" that was returned by WinBatch's "Zipper"
          Extender (WWZIP44I.DLL), see the web site referenced for this extender
          in the "Distributed Files" section above.

        V) For unknown reasons, at least one of your relevant Standard Name
          Extended (SNE) Playback (.LPB) files could not be backed up by
          appending "_Old" (without the quotes) to the right side of their root
          names.  This could have produced an IRREVERSIBLE LOSS of these files
          (see the Warning Note below).  Also, since they were not updated, any
          existing SNE Playback (.LPB) backup files probably became invalid.

        W) Although at least one backup method was selected, no relevant
          LaserTank .LVL, .GHS, .HS, "Report" .HTML, or SNE Playback (.LPB)
          files were found (in YourLTFolder) to backup.  Hence, no backup
          operations were performed.  Since they were not updated, any existing
          backup files for these files (with root names ending with "_Old" -
          without the quotes), and/or the files in an existing LaserTank Data
          Backup Archive file (YourLTFolder\LTDataBackup.ZIP), probably became
          invalid.

        X) For unknown reasons, at least one of your relevant Standard Name
          Extended (SNE) Playback (.LPB) files could not be moved to the
          ~TempLTUDU subfolder of YourLTFolder.  This could have produced
          ERRONEOUS RESULTS for, and/or IRREVERSIBLE LOSS of, playback
          information for Moved levels (if any).  (See the Warning Note below.)

        Y) For unknown reasons, at least one of your relevant Standard Name
          Extended (SNE) Playback (.LPB) files could not be deleted, renamed or
          moved.  This could have produced ERRONEOUS RESULTS for, and/or
          IRREVERSIBLE LOSS of, playback information (see the Warning Note
          below).

     Warning Note: The term "IRREVERSIBLE LOSS" in these warnings only applies
  if you, or the LTUDU program (LTUpdate.exe), did not backup your LaserTank
  data files as described in Op(eration)s 1 and 2 in the "What the LTUDU Program
  Does", and Note 6 in the "Notes", sections above.  Hence, it's HIGHLY
  RECOMMENDED that you select at least one of the backup methods offered 
  Op(eration) 1.

  ------------------------------------------------------------------------------


  ========================================
  Installation and Removal of This Utility
  ========================================

     Although the LTUDU program (or similar) has only been tested on Windows 10
  and earlier systems, it should run on any 32-bit, or higher, Windows platform.
  A vertical screen resolution of at least 900 pixels is required to fully
  display some of the dialog boxes generated by this program.

     To install this utility, just extract the LTUpdate.exe, WBDXA44I.DLL and
  WWZIP44I.DLL files (see Note 13 below), and (optionally - see below) the
  ReadMeUDU.TXT file, from the LTUpdate.ZIP archive to YourLTFolder (see the
  "Description of the LTUDU Program" section above).  The old WBDCC34I.DLL and
  WWZIP34I.DLL files are no longer required; so, you can remove them from
  YourLTFolder.  WARNING: If these are not in YourLTFolder, they may be used by
  other programs on your computer; so, you may not want to delete them.  This
  WBDXA44I.DLL file is identical to the one required by the LaserTank Utilities,
  and Developer's Utilities, Suites (LTUS and LTDUS, respectively).

     The ReadMeUDU.TXT file is required only if you click on the "Open
  ReadMeUDU.TXT" button in a displayed dialog box.  The VerHistUDU.TXT file in
  the LTUpdate.ZIP archive is for information only.  So, you don't need to
  un-Zip it.

     You can create shortcuts (e.g., in your Start menu, or on your Desktop) to
  the LTUpdate.exe program in the usual manner (see your Windows documentation -
  Ha, Ha, Ha).  The "Start in" folder of these shortcuts must be YourLTFolder.
  The LTUpdate.exe program can also be run directly in the usual manner (e.g.,
  by double-clicking its listing or icon in the Windows Explorer).

     Optional: You may move the WBDXA44I.DLL and WWZIP44I.DLL files to any
  folder that is in your search PATH (Environment variable); e.g., your Windows
  folder.  Do not overwrite existing, newer versions, of these files.  Then you
  can place LTUpdate.exe anywhere on your hard drive.

     To uninstall this utility, just delete the files that you extracted to
  install it (e.g., LTUpdate.exe, WBDXA44I.DLL, WWZIP44I.DLL and ReadMeUDU.TXT),
  and remove the shortcuts that you created (if any).  WARNING: If the
  WBDXA44I.DLL and WWZIP44I.DLL files are not in YourLTFolder, they may be used
  by other programs on your computer; so, you may not want to delete them.

  ------------------------------------------------------------------------------


  ===============
  Version History
  ===============

     See the VerHistUDU.TXT file in the downloaded LTUpdates.zip archive file.

  ------------------------------------------------------------------------------


  =========================
  Liability and Legal Stuff
  =========================

     This software is provided "AS IS", and without any expressed and/or implied
  warranties of any kind.  The author of this software will not take any
  responsibilities for any damages that may occur with its use.  The user
  assumes the entire risk of using this software.

     This software may not be distributed as part of any commercial product
  (including "shareware") without a prior license agreement with the author.
  All rights remain with the author.  See the next section for the email address
  of the author.

  ------------------------------------------------------------------------------


  =====================
  Contacting the Author
  =====================

     If you have questions, comments, enhancement requests, improvement
  suggestions or bug reports about this utility or its documentation, you can
  e-mail me (in English please) at DrWiseKing@Comcast.net.

  ------------------------------------------------------------------------------


        Enjoy,
        Harvey Solomon (DrWiseKing)
        Woodstock, Maryland, USA 21163

  ------------------------------------------------------------------------------
