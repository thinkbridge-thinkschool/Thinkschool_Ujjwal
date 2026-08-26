import { Component, inject, signal } from '@angular/core';
import { QuotesComponent } from './components/quotes/quotes';
import { QuoteFormComponent } from './components/quote-form/quote-form';
import { QuoteFormSignalComponent } from './components/quote-form-signal/quote-form-signal';
import { LoginComponent } from './components/login/login';
import { AuthService } from './services/auth';

type View = 'quotes' | 'create' | 'create-signal';

@Component({
  selector: 'app-root',
  imports: [QuotesComponent, QuoteFormComponent, QuoteFormSignalComponent, LoginComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);

  protected readonly view = signal<View>('quotes');

  setView(view: View): void {
    this.view.set(view);
  }

  onQuoteCreated(): void {
    // quoteCreated.emit() runs synchronously inside the same handler that
    // sets QuoteFormComponent's state to 'success', so switching the view
    // immediately unmounts the form before its "Quote added." message ever
    // paints a frame - found live (Playwright screenshot showed no success
    // text despite the quote being created). Delay handing back to the list
    // view so the confirmation is actually visible first.
    setTimeout(() => this.view.set('quotes'), 900);
  }
}
