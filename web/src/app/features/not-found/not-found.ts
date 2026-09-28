import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-not-found',
  imports: [TranslocoPipe],
  templateUrl: './not-found.html',
  styleUrl: './not-found.scss',
})
export class NotFound {}
