# blag-gen
A simple static page generator

![](xkcd.png)

https://xkcd.com/148/

https://www.explainxkcd.com/wiki/index.php/148:_Mispronouncing

# usp
* Frontmatter looks like markdown so pure markdown previews won't look bad
* Custom template engine inspired by mustache and hugo templates.

# todo

Perhaps there is some order in this list but it's mostly so I can remember what features I think are missing and I want to add

* Support having a different dist folder layout than the source layout
* Watch mode/Hot-reload
  - File watcher/Generate files when saved
  - Create a webserver
  - Reload pages when generated
* Copy meta-files such as images
* Copy static files
* Implement markdeep?
* Add verification steps to make sure code samples can be compiled and (unit) tested
  - compile code "without errors"
  - use some sort of shebang to annotate what code pieces are used
  - merge several code pieces to a single executable
  - use patterns to create executable (generate cmake files for example)
  - test and run and check against code output in another code sample or special comments in the original sample
* Add support for generating images using code
  - for example using processing
  - custom code that outputs images or svg

# A rough plan
This isn't really plan, just a rough sketch to figure out when the goals might be completed...

```mermaid
gantt
    dateFormat  YYYY-MM-DD
    section Plan
    excludes monday, tuesday, wednesday, thursday

    section Tags (done)
    Generate tag pages: done, tag_pages, 2023-06-19, 2d
    List tags: done, list_tags, after tag_pages, 1d
    Tag command: done, tag_command, after list_tags, 1d
    Tags done: done, tags_done, after tag_command, 0h

    section Hot reload
    File watcher: done, file_watcher, after tags_done, 1d
    Webserver: done, webserver, after file_watcher, 1d
    Create pages: done, create_pages, after webserver, 1d
    Reload pages: reload_pages, after create_pages, 1d
    Hot reload done: hotreload_done, after reload_pages, 0d
    
    section Actions
    Add verification steps: action_verify, after hotreload_done, 3d
    Add support for generating umages using code: action_image, after action_verify, 2d

    section Other
    Copy meta files: copy_meta, after action_image, 1d
    Copy static files: copy_static, after copy_meta, 1d

    Profile against existing blogs: after copy_static, 10d
```